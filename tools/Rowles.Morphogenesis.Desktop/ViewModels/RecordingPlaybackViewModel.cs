using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rowles.Morphogenesis.Desktop.Networking;

namespace Rowles.Morphogenesis.Desktop.ViewModels;

public sealed partial class RecordingPlaybackViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaximumCachedFrames = 3;
    private readonly Guid _sessionId;
    private readonly RecordingDto _recording;
    private readonly RecordedFrameClient _frameClient;
    private readonly Action<FullFrameBuffer> _showFrame;
    private readonly object _cacheGate = new();
    private readonly Dictionary<long, CacheEntry> _cache = [];
    private readonly LinkedList<long> _cacheOrder = [];
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _playbackCancellation;
    private Task? _playbackTask;

    public RecordingPlaybackViewModel(
        Guid sessionId,
        RecordingDto recording,
        RecordedFrameClient frameClient,
        Action<FullFrameBuffer> showFrame)
    {
        _sessionId = sessionId;
        _recording = recording;
        _frameClient = frameClient;
        _showFrame = showFrame;
        foreach (RecordingFrameDto frame in recording.Frames.OrderBy(frame => frame.Sequence))
            Frames.Add(frame);
        SelectedFrameMcs = Frames.FirstOrDefault()?.Mcs ?? 0;
    }

    public ObservableCollection<RecordingFrameDto> Frames { get; } = [];
    public IReadOnlyList<double> PlaybackRateOptions { get; } = [0.25, 0.5, 1, 2, 4];

    [ObservableProperty]
    private long _selectedFrameMcs;

    [ObservableProperty]
    private double _playbackRate = 1;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private string _status = "Recording ready.";

    public int CachedFrameCount
    {
        get
        {
            lock (_cacheGate)
                return _cache.Count;
        }
    }
    public long? FirstMcs => _recording.FirstMcs;
    public long? LastMcs => _recording.LastMcs;

    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        if (Frames.Count > 0)
            await SeekAsync(Frames[0].Mcs, cancellationToken);
    }

    [RelayCommand]
    private Task PlayAsync(CancellationToken cancellationToken)
    {
        if (IsPlaying || Frames.Count == 0)
            return Task.CompletedTask;
        _playbackCancellation?.Dispose();
        _playbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        IsPlaying = true;
        _playbackTask = RunPlaybackAsync(_playbackCancellation.Token);
        return _playbackTask;
    }

    [RelayCommand]
    private void Pause()
    {
        _playbackCancellation?.Cancel();
        IsPlaying = false;
    }

    [RelayCommand]
    private async Task StepForwardAsync(CancellationToken cancellationToken) =>
        await StepAsync(1, cancellationToken);

    [RelayCommand]
    private async Task StepBackwardAsync(CancellationToken cancellationToken) =>
        await StepAsync(-1, cancellationToken);

    [RelayCommand]
    private async Task SeekCommandAsync(CancellationToken cancellationToken) =>
        await SeekAsync(SelectedFrameMcs, cancellationToken);

    public async Task SeekAsync(long mcs, CancellationToken cancellationToken = default)
    {
        if (Frames.Count == 0)
            return;
        int index = FindFrameIndex(mcs);
        RecordingFrameDto target = Frames[index];
        SelectedFrameMcs = target.Mcs;
        try
        {
            FullFrameBuffer frame = await GetCachedFrameAsync(target.Mcs, cancellationToken);
            _showFrame(frame);
            Status = $"Showing recorded frame at MCS {frame.Header.Mcs}.";
            PrefetchNeighbours(index, _lifetime.Token);
        }
        catch (Exception exception) when (exception is LaboratoryApiException or HttpRequestException or OperationCanceledException or InvalidDataException)
        {
            Status = exception.Message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        Pause();
        if (_playbackTask is not null)
            await _playbackTask;
        if (_playbackCancellation is not null)
        {
            _playbackCancellation.Dispose();
            _playbackCancellation = null;
        }
        _lifetime.Dispose();
    }

    private async Task RunPlaybackAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.1 / PlaybackRate), cancellationToken);
                int index = FindFrameIndex(SelectedFrameMcs);
                if (index + 1 >= Frames.Count)
                {
                    Pause();
                    return;
                }
                await SeekAsync(Frames[index + 1].Mcs, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsPlaying = false;
        }
    }

    private async Task StepAsync(int direction, CancellationToken cancellationToken)
    {
        if (Frames.Count == 0)
            return;
        int index = FindFrameIndex(SelectedFrameMcs);
        int next = Math.Clamp(index + direction, 0, Frames.Count - 1);
        await SeekAsync(Frames[next].Mcs, cancellationToken);
    }

    private async Task<FullFrameBuffer> GetCachedFrameAsync(long mcs, CancellationToken cancellationToken)
    {
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(mcs, out CacheEntry? cached))
            {
                _cacheOrder.Remove(cached.Node);
                _cacheOrder.AddFirst(cached.Node);
                return cached.Frame;
            }
        }

        FullFrameBuffer frame = await _frameClient.GetFrameAsync(_sessionId, mcs, cancellationToken);
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(frame.Header.Mcs, out CacheEntry? existing))
            {
                _cacheOrder.Remove(existing.Node);
                _cacheOrder.AddFirst(existing.Node);
                return existing.Frame;
            }

            LinkedListNode<long> node = _cacheOrder.AddFirst(frame.Header.Mcs);
            _cache.Add(frame.Header.Mcs, new CacheEntry(frame, node));
            while (_cache.Count > MaximumCachedFrames)
            {
                LinkedListNode<long> oldest = _cacheOrder.Last!;
                _cacheOrder.RemoveLast();
                _cache.Remove(oldest.Value);
            }
        }
        Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(CachedFrameCount)));
        return frame;
    }

    private void PrefetchNeighbours(int index, CancellationToken cancellationToken)
    {
        if (index > 0)
            _ = PrefetchAsync(Frames[index - 1].Mcs, cancellationToken);
        if (index + 1 < Frames.Count)
            _ = PrefetchAsync(Frames[index + 1].Mcs, cancellationToken);
    }

    private async Task PrefetchAsync(long mcs, CancellationToken cancellationToken)
    {
        try
        {
            await GetCachedFrameAsync(mcs, cancellationToken);
        }
        catch (Exception exception) when (exception is LaboratoryApiException or HttpRequestException or OperationCanceledException or InvalidDataException)
        {
        }
    }

    private int FindFrameIndex(long mcs)
    {
        int best = 0;
        for (int index = 1; index < Frames.Count && Frames[index].Mcs <= mcs; index++)
            best = index;
        return best;
    }

    private sealed record CacheEntry(FullFrameBuffer Frame, LinkedListNode<long> Node);
}
