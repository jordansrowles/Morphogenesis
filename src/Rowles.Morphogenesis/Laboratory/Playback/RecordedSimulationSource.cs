using Rowles.Morphogenesis.Laboratory.Publication;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;

namespace Rowles.Morphogenesis.Laboratory.Playback;

public sealed class RecordedSimulationSource : ISimulationSource
{
    private static readonly SimulationSourceCapabilities SourceCapabilities = new(
        IsLive: false,
        CanSeek: true,
        CanReverse: true,
        CanControlExecution: false,
        CanChangePlaybackRate: true);
    private static readonly double[] SupportedRates = [0.25, 0.5, 1, 2, 4];
    private readonly object _playbackGate = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly IRecordingReader _reader;
    private readonly Guid _sessionId;
    private readonly RecordingHeader _header;
    private readonly RecordingFrameIndexEntry[] _frameIndex;
    private readonly int[] _lattice;
    private readonly int[] _deltaIndices;
    private readonly int[] _deltaCellIds;
    private readonly FrameBufferPool _framePool;
    private readonly LatestFrameHub _frameHub;
    private CancellationTokenSource? _playbackCancellation;
    private Task? _playbackTask;
    private int _currentFramePosition = -1;
    private long _currentMcs = -1;
    private long _publishedSequence;
    private double _playbackRate = 1;
    private int _disposed;

    private RecordedSimulationSource(
        IRecordingReader reader,
        Guid sessionId,
        RecordingHeader header,
        RecordingFrameIndexEntry[] frameIndex)
    {
        _reader = reader;
        _sessionId = sessionId;
        _header = header;
        _frameIndex = frameIndex;
        _lattice = new int[checked(header.Width * header.Height)];
        _deltaIndices = new int[_lattice.Length];
        _deltaCellIds = new int[_lattice.Length];
        _framePool = new FrameBufferPool(bufferCount: 10, siteCount: _lattice.Length);
        _frameHub = new LatestFrameHub(sessionId, header.Width, header.Height, maximumSubscribers: 8, static () => { });
    }

    public SimulationMetadata Metadata => _header.Metadata;

    public SimulationSourceCapabilities Capabilities => SourceCapabilities;

    public long CurrentMcs => Volatile.Read(ref _currentMcs);

    public double PlaybackRate
    {
        get
        {
            lock (_playbackGate)
            {
                return _playbackRate;
            }
        }
    }

    public long CurrentRecordedSequence =>
        _currentFramePosition >= 0 ? _frameIndex[_currentFramePosition].Sequence : -1;

    public string? PlaybackFailure { get; private set; }

    public static async ValueTask<RecordedSimulationSource> OpenAsync(
        IRecordingReader reader,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        RecordingHeader header = await reader.GetHeaderAsync(sessionId, cancellationToken).ConfigureAwait(false);
        ValidateHeader(header, sessionId);
        IReadOnlyList<RecordingFrameIndexEntry> sourceIndex = await reader.GetFrameIndexAsync(sessionId, cancellationToken).ConfigureAwait(false);
        RecordingFrameIndexEntry[] frameIndex = sourceIndex.ToArray();
        ValidateFrameIndex(frameIndex);
        RecordedSimulationSource source = new(reader, sessionId, header, frameIndex);
        try
        {
            await source.SeekFramePositionAsync(0, cancellationToken).ConfigureAwait(false);
            return source;
        }
        catch
        {
            await source.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<SimulationFrameLease> GetCurrentFrameAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_currentFramePosition < 0)
            {
                throw new InvalidOperationException("The recorded source has no current frame.");
            }

            if (!_framePool.TryAcquire(out FrameBufferOwner? owner) || owner is null)
            {
                throw new InvalidOperationException("All recorded-source frame buffers are currently leased.");
            }

            try
            {
                _lattice.CopyTo(owner.CellIds, 0);
                return new SimulationFrameLease(
                    _sessionId,
                    _publishedSequence,
                    _currentMcs,
                    _header.Width,
                    _header.Height,
                    owner);
            }
            catch
            {
                owner.Release();
                throw;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public IAsyncEnumerable<SimulationFrameLease> WatchFramesAsync(CancellationToken cancellationToken = default) =>
        _frameHub.Watch(cancellationToken);

    public ValueTask PlayAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_playbackGate)
        {
            if (_playbackTask is { IsCompleted: false })
            {
                return ValueTask.CompletedTask;
            }

            _playbackCancellation?.Dispose();
            _playbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _playbackTask = PlaybackLoopAsync(_playbackCancellation.Token);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        Task? playbackTask;
        lock (_playbackGate)
        {
            _playbackCancellation?.Cancel();
            playbackTask = _playbackTask;
        }

        if (playbackTask is not null)
        {
            await playbackTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask SeekAsync(long mcs, CancellationToken cancellationToken = default)
    {
        if (mcs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mcs));
        }

        await PauseAsync(cancellationToken).ConfigureAwait(false);
        int targetPosition = FindLastFrameAtOrBefore(mcs);
        if (targetPosition < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mcs), "No recorded frame exists at or before the requested MCS.");
        }

        RecordingFrameIndexEntry? keyframe = await _reader.FindNearestKeyframeAtOrBeforeAsync(
            _sessionId,
            mcs,
            cancellationToken).ConfigureAwait(false);
        if (keyframe is null)
        {
            throw new FormatException("The recording has no keyframe at or before the requested MCS.");
        }

        int keyframePosition = FindPositionBySequence(keyframe.Sequence);
        if (keyframePosition < 0 || _frameIndex[keyframePosition].Kind != RecordingFrameKind.Keyframe || keyframePosition > targetPosition)
        {
            throw new FormatException("The recording reader returned an invalid nearest keyframe.");
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReconstructFromKeyframeAsync(keyframePosition, targetPosition, cancellationToken).ConfigureAwait(false);
            _currentFramePosition = targetPosition;
            _currentMcs = _frameIndex[targetPosition].Mcs;
            PublishCurrentFrame();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask StepForwardAsync(CancellationToken cancellationToken = default)
    {
        await PauseAsync(cancellationToken).ConfigureAwait(false);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int nextPosition = _currentFramePosition + 1;
            if (nextPosition < _frameIndex.Length)
            {
                await ApplyFrameAtPositionAsync(nextPosition, cancellationToken).ConfigureAwait(false);
                _currentFramePosition = nextPosition;
                _currentMcs = _frameIndex[nextPosition].Mcs;
                PublishCurrentFrame();
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask StepBackwardAsync(CancellationToken cancellationToken = default)
    {
        await PauseAsync(cancellationToken).ConfigureAwait(false);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int previousPosition = _currentFramePosition - 1;
            if (previousPosition >= 0)
            {
                int keyframePosition = FindPreviousKeyframe(previousPosition);
                await ReconstructFromKeyframeAsync(keyframePosition, previousPosition, cancellationToken).ConfigureAwait(false);
                _currentFramePosition = previousPosition;
                _currentMcs = _frameIndex[previousPosition].Mcs;
                PublishCurrentFrame();
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void SetPlaybackRate(double playbackRate)
    {
        if (!SupportedRates.Contains(playbackRate))
        {
            throw new ArgumentOutOfRangeException(nameof(playbackRate), "Playback rate must be 0.25, 0.5, 1, 2, or 4.");
        }

        lock (_playbackGate)
        {
            _playbackRate = playbackRate;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await PauseAsync().ConfigureAwait(false);
        _frameHub.Dispose();
        _framePool.Dispose();
        _playbackCancellation?.Dispose();
        _operationGate.Dispose();
    }

    private async Task PlaybackLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                double rate = PlaybackRate;
                await Task.Delay(TimeSpan.FromSeconds(1d / (10d * rate)), cancellationToken).ConfigureAwait(false);
                await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    int nextPosition = _currentFramePosition + 1;
                    if (nextPosition >= _frameIndex.Length)
                    {
                        return;
                    }

                    await ApplyFrameAtPositionAsync(nextPosition, cancellationToken).ConfigureAwait(false);
                    _currentFramePosition = nextPosition;
                    _currentMcs = _frameIndex[nextPosition].Mcs;
                    PublishCurrentFrame();
                }
                finally
                {
                    _operationGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            PlaybackFailure = $"{exception.GetType().Name}: {exception.Message}";
        }
    }

    private async Task SeekFramePositionAsync(int targetPosition, CancellationToken cancellationToken)
    {
        int keyframePosition = FindPreviousKeyframe(targetPosition);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReconstructFromKeyframeAsync(keyframePosition, targetPosition, cancellationToken).ConfigureAwait(false);
            _currentFramePosition = targetPosition;
            _currentMcs = _frameIndex[targetPosition].Mcs;
            PublishCurrentFrame();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ReconstructFromKeyframeAsync(int keyframePosition, int targetPosition, CancellationToken cancellationToken)
    {
        await ApplyFrameAtPositionAsync(keyframePosition, cancellationToken).ConfigureAwait(false);
        for (int position = keyframePosition + 1; position <= targetPosition; position++)
        {
            await ApplyFrameAtPositionAsync(position, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyFrameAtPositionAsync(int position, CancellationToken cancellationToken)
    {
        RecordingFrameIndexEntry expected = _frameIndex[position];
        EncodedRecordingFrame frame = await _reader.ReadFrameAsync(_sessionId, expected.Sequence, cancellationToken).ConfigureAwait(false);
        RecordingFrameReconstructor.DecodeFrame(frame, _header, expected, _lattice, _deltaIndices, _deltaCellIds, _lattice.Length);
    }

    private void PublishCurrentFrame()
    {
        if (!_framePool.TryAcquire(out FrameBufferOwner? owner) || owner is null)
        {
            throw new InvalidOperationException("The recorded frame publisher exhausted its reusable buffers.");
        }

        _lattice.CopyTo(owner.CellIds, 0);
        long sequence = ++_publishedSequence;
        try
        {
            if (!_frameHub.Publish(owner, sequence, _currentMcs))
            {
                throw new ObjectDisposedException(nameof(LatestFrameHub));
            }
        }
        finally
        {
            owner.Release();
        }
    }

    private int FindLastFrameAtOrBefore(long mcs)
    {
        int result = -1;
        for (int index = 0; index < _frameIndex.Length && _frameIndex[index].Mcs <= mcs; index++)
        {
            result = index;
        }

        return result;
    }

    private int FindPositionBySequence(long sequence)
    {
        for (int index = 0; index < _frameIndex.Length; index++)
        {
            if (_frameIndex[index].Sequence == sequence)
            {
                return index;
            }
        }

        return -1;
    }

    private int FindPreviousKeyframe(int targetPosition)
    {
        for (int position = targetPosition; position >= 0; position--)
        {
            if (_frameIndex[position].Kind == RecordingFrameKind.Keyframe)
            {
                return position;
            }
        }

        throw new FormatException("The recorded frame sequence has no keyframe before the target.");
    }

    internal static void ValidateHeader(RecordingHeader header, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (header.RecordingSchemaVersion != RecordingFormat.SchemaVersion ||
            header.SessionId != sessionId ||
            header.EnvelopeVersion != RecordingFormat.EnvelopeVersion ||
            header.KeyframeCodecVersion != RecordingFormat.KeyframeCodecVersion ||
            header.DeltaCodecVersion != RecordingFormat.DeltaCodecVersion ||
            header.Compression != RecordingFormat.Compression)
        {
            throw new NotSupportedException("The recording header uses an unsupported schema, codec, or compression version.");
        }

        if (header.Width <= 0 || header.Height <= 0 ||
            header.Metadata.SessionId != sessionId ||
            header.Metadata.GridWidth != header.Width ||
            header.Metadata.GridHeight != header.Height ||
            header.ExperimentManifestJson.Length == 0)
        {
            throw new FormatException("The recording header contains invalid session or lattice metadata.");
        }
    }

    internal static void ValidateFrameIndex(RecordingFrameIndexEntry[] frameIndex)
    {
        if (frameIndex.Length == 0 || frameIndex[0].Sequence != 0 || frameIndex[0].Mcs != 0 ||
            frameIndex[0].Kind != RecordingFrameKind.Keyframe)
        {
            throw new FormatException("A recording must start with an MCS 0 keyframe at sequence zero.");
        }

        for (int index = 0; index < frameIndex.Length; index++)
        {
            if (frameIndex[index].Sequence != index || frameIndex[index].Mcs < 0 ||
                (index > 0 && frameIndex[index].Mcs < frameIndex[index - 1].Mcs) ||
                !Enum.IsDefined(frameIndex[index].Kind))
            {
                throw new FormatException("The recording frame index is not a valid increasing sequence.");
            }
        }
    }
}
