using System.Collections.ObjectModel;
using System.Net.WebSockets;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rowles.Morphogenesis.Desktop.Networking;
using Rowles.Morphogenesis.Desktop.Rendering;

namespace Rowles.Morphogenesis.Desktop.ViewModels;

public sealed partial class SessionViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ILaboratoryApiClient _apiClient;
    private readonly ILiveFrameStreamClient _streamClient;
    private readonly RecordedFrameClient _recordedFrameClient;
    private readonly LatticeBitmapRenderer _renderer = new();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _streamCancellation;
    private Task? _streamTask;
    private Task? _refreshTask;
    private FullFrameBuffer? _displayedFrame;
    private bool _disposed;
    private bool _dragging;
    private bool _dragMoved;
    private Point _dragStart;
    private double _dragPanX;
    private double _dragPanY;

    public SessionViewModel(SessionDto session, ILaboratoryApiClient apiClient, ILiveFrameStreamClient streamClient)
    {
        Session = session;
        _apiClient = apiClient;
        _streamClient = streamClient;
        _recordedFrameClient = new RecordedFrameClient(apiClient);
        Inspector = new CellInspectorViewModel(apiClient, session.SessionId);
        PlaybackRateOptions = [0.25, 0.5, 1, 2, 4];
        RefreshCommandAvailability();
        ImageTransform = new MatrixTransform(Matrix.Identity);
    }

    [ObservableProperty]
    private SessionDto _session;

    [ObservableProperty]
    private WriteableBitmap? _bitmap;

    [ObservableProperty]
    private string _streamStatus = "Not connected";

    [ObservableProperty]
    private string _commandStatus = string.Empty;

    [ObservableProperty]
    private LatticeViewMode _viewMode = LatticeViewMode.CellIdentity;

    [ObservableProperty]
    private MatrixTransform _imageTransform;

    [ObservableProperty]
    private double _zoom = 1;

    [ObservableProperty]
    private double _panX;

    [ObservableProperty]
    private double _panY;

    [ObservableProperty]
    private bool _playbackVisible;

    [ObservableProperty]
    private IReadOnlyList<MetricSeries> _heterotypicSeries = [];

    [ObservableProperty]
    private IReadOnlyList<MetricSeries> _areaSeries = [];

    [ObservableProperty]
    private IReadOnlyList<MetricSeries> _perimeterSeries = [];

    [ObservableProperty]
    private IReadOnlyList<MetricSeries> _cellCountSeries = [];

    public CellInspectorViewModel Inspector { get; }
    public RecordingPlaybackViewModel? Playback { get; private set; }
    public bool HasPlayback => Playback is not null;
    public IReadOnlyList<double> PlaybackRateOptions { get; }
    public IReadOnlyList<LatticeViewMode> ViewModes { get; } = Enum.GetValues<LatticeViewMode>();
    public string SessionStatusText => Session.Status.ToString();
    public string McsText => $"MCS {Session.CurrentMcs:N0} / {Session.TargetMcs:N0}";
    public string RevisionText => $"Revision {Session.Revision}";
    public string RecordingStatusText => Session.RecordingFailure is null
        ? Session.RecordingState.ToString()
        : $"{Session.RecordingState}: {Session.RecordingFailure}";
    public bool CanStart => Session.Status == SessionStatusDto.Created;
    public bool CanPause => Session.Status == SessionStatusDto.Running;
    public bool CanResume => Session.Status == SessionStatusDto.Paused;
    public bool CanStep => Session.Status == SessionStatusDto.Paused;
    public bool CanStop => Session.Status is SessionStatusDto.Created or SessionStatusDto.Running or SessionStatusDto.Paused;
    public bool IsTerminal => Session.Status is SessionStatusDto.Completed or SessionStatusDto.Failed or
        SessionStatusDto.Interrupted or SessionStatusDto.Cancelled;

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        Session = await _apiClient.GetSessionAsync(Session.SessionId, cancellationToken);
        RefreshCommandAvailability();
        await RefreshMetricsAsync(cancellationToken);
        await LoadRecordingAsync(cancellationToken);
        if (!IsTerminal)
            await ReconnectAsync(cancellationToken);
        if (!IsTerminal)
            _refreshTask = RunRefreshLoopAsync(_lifetime.Token);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync(CancellationToken cancellationToken) => SendCommandAsync("start", cancellationToken);

    [RelayCommand(CanExecute = nameof(CanPause))]
    private Task PauseAsync(CancellationToken cancellationToken) => SendCommandAsync("pause", cancellationToken);

    [RelayCommand(CanExecute = nameof(CanResume))]
    private Task ResumeAsync(CancellationToken cancellationToken) => SendCommandAsync("resume", cancellationToken);

    [RelayCommand(CanExecute = nameof(CanStep))]
    private Task StepAsync(CancellationToken cancellationToken) => SendCommandAsync("step", cancellationToken);

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopAsync(CancellationToken cancellationToken) => SendCommandAsync("stop", cancellationToken);

    [RelayCommand]
    private async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
            return;
        await DisconnectStreamAsync();
        try
        {
            StreamStatus = "Refreshing session state…";
            Session = await _apiClient.GetSessionAsync(Session.SessionId, cancellationToken);
            RefreshCommandAvailability();
            if (IsTerminal)
            {
                StreamStatus = "Session is no longer live.";
                await LoadRecordingAsync(cancellationToken);
                return;
            }

            _streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
            StreamStatus = "Connecting…";
            _streamTask = RunStreamAsync(_streamCancellation.Token);
        }
        catch (Exception exception) when (exception is LaboratoryApiException or HttpRequestException or TaskCanceledException)
        {
            StreamStatus = $"Disconnected: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task ShowPlaybackAsync(CancellationToken cancellationToken)
    {
        if (Playback is null)
            await LoadRecordingAsync(cancellationToken);
        PlaybackVisible = Playback is not null;
    }

    public async Task DisconnectStreamAsync()
    {
        CancellationTokenSource? cancellation = _streamCancellation;
        Task? stream = _streamTask;
        _streamCancellation = null;
        _streamTask = null;
        cancellation?.Cancel();
        if (stream is not null)
            await stream;
        cancellation?.Dispose();
        if (!_disposed)
            StreamStatus = "Disconnected";
    }

    public void SetViewMode(LatticeViewMode mode)
    {
        ViewMode = mode;
        RenderDisplayedFrame();
    }

    public void BeginPointer(Point point)
    {
        _dragging = true;
        _dragMoved = false;
        _dragStart = point;
        _dragPanX = PanX;
        _dragPanY = PanY;
    }

    public void MovePointer(Point point)
    {
        if (!_dragging)
            return;
        double dx = point.X - _dragStart.X;
        double dy = point.Y - _dragStart.Y;
        if (Math.Abs(dx) + Math.Abs(dy) > 2)
            _dragMoved = true;
        if (_dragMoved)
        {
            PanX = _dragPanX + dx;
            PanY = _dragPanY + dy;
            UpdateImageTransform();
        }
    }

    public async Task EndPointerAsync(Point point, double viewportWidth, double viewportHeight, CancellationToken cancellationToken = default)
    {
        bool select = _dragging && !_dragMoved;
        _dragging = false;
        if (select)
            await SelectAtAsync(point.X, point.Y, viewportWidth, viewportHeight, cancellationToken);
    }

    public void ZoomAt(Point point, double wheelDelta, double viewportWidth, double viewportHeight)
    {
        if (_displayedFrame is null)
            return;
        double oldZoom = Zoom;
        double newZoom = Math.Clamp(oldZoom * (wheelDelta > 0 ? 1.15 : 1 / 1.15), 1, 32);
        if (newZoom == oldZoom)
            return;
        (double scale, double offsetX, double offsetY) = GetFit(viewportWidth, viewportHeight);
        double latticeX = (point.X - PanX - offsetX * oldZoom) / (scale * oldZoom);
        double latticeY = (point.Y - PanY - offsetY * oldZoom) / (scale * oldZoom);
        PanX = point.X - (offsetX + latticeX * scale) * newZoom;
        PanY = point.Y - (offsetY + latticeY * scale) * newZoom;
        Zoom = newZoom;
        UpdateImageTransform();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetime.Cancel();
        if (_refreshTask is not null)
            await _refreshTask;
        await DisconnectStreamAsync();
        if (Playback is not null)
            await Playback.DisposeAsync();
        _streamCancellation?.Dispose();
        _lifetime.Dispose();
        _renderer.Dispose();
    }

    private async Task SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            CommandResponse response = await _apiClient.SendCommandAsync(
                Session.SessionId,
                command,
                new SessionCommandRequestDto(Guid.NewGuid(), Session.Revision),
                cancellationToken);
            if (response.NotFound)
            {
                CommandStatus = "The server no longer has this session.";
                return;
            }
            if (response.Session is not null)
                Session = response.Session;
            if (response.Failed)
                CommandStatus = response.Error ?? $"{command} failed; the session is {Session.Status}.";
            else if (response.Conflict)
                CommandStatus = "Conflict: server state was refreshed. Review it before sending another command.";
            else
                CommandStatus = $"{command} applied.";
            RefreshCommandAvailability();
            await RefreshMetricsAsync(cancellationToken);
            if (IsTerminal)
                await LoadRecordingAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is LaboratoryApiException or HttpRequestException or TaskCanceledException)
        {
            CommandStatus = exception.Message;
        }
    }

    private async Task RunStreamAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _streamClient.StreamAsync(
                _apiClient.GetStreamUri(Session.SessionId),
                Session.Width,
                Session.Height,
                async (frame, token) =>
                {
                    Action applyFrame = () =>
                    {
                        _displayedFrame = frame;
                        Bitmap = _renderer.Render(frame, Session.Metadata, ViewMode);
                        StreamStatus = "Connected";
                        Inspector.UpdateDisplayedFrameMcs(frame.Header.Mcs);
                        OnPropertyChanged(nameof(McsText));
                    };
                    await Dispatcher.UIThread.InvokeAsync(applyFrame, DispatcherPriority.Normal, token);
                },
                cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                StreamStatus = "Disconnected";
                await RefreshAuthoritativeStateAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is WebSocketException or HttpRequestException or InvalidDataException or IOException)
        {
            if (!cancellationToken.IsCancellationRequested)
                StreamStatus = $"Disconnected: {exception.Message}";
        }
    }

    private async Task LoadRecordingAsync(CancellationToken cancellationToken)
    {
        if (Session.RecordingState is not (RecordingStateDto.Completed or RecordingStateDto.Active))
            return;
        try
        {
            RecordingDto recording = await _apiClient.GetRecordingAsync(Session.SessionId, cancellationToken);
            if (recording.FrameCount == 0)
                return;
            if (Playback is not null)
                await Playback.DisposeAsync();
            Playback = new RecordingPlaybackViewModel(Session.SessionId, recording, _recordedFrameClient, frame =>
            {
                _displayedFrame = frame;
                Bitmap = _renderer.Render(frame, Session.Metadata, ViewMode);
                Inspector.UpdateDisplayedFrameMcs(frame.Header.Mcs);
                OnPropertyChanged(nameof(McsText));
            });
            OnPropertyChanged(nameof(Playback));
            OnPropertyChanged(nameof(HasPlayback));
            await Playback.InitialiseAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is LaboratoryApiException or HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            CommandStatus = $"Recording unavailable: {exception.Message}";
        }
    }

    private async Task RefreshMetricsAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<PersistedMetricSampleDto> samples = await _apiClient.GetMetricsAsync(Session.SessionId, cancellationToken)
                ;
            MetricPoint[] heterotypic = GetPoints(samples, "heterotypic-interface-fraction");
            MetricPoint[] areas = GetPoints(samples, "cell-area.mean");
            MetricPoint[] perimeters = GetPoints(samples, "cell-perimeter.mean");
            MetricSeries[] cellCounts =
            [
                new("Type A", new RgbColour(54, 82, 196), GetPoints(samples, "type-a-cell-count")),
                new("Type B", new RgbColour(224, 112, 58), GetPoints(samples, "type-b-cell-count"))
            ];
            HeterotypicSeries = [new MetricSeries("Heterotypic interface fraction", new RgbColour(41, 154, 112), heterotypic)];
            AreaSeries = [new MetricSeries("Mean cell area", new RgbColour(121, 101, 181), areas)];
            PerimeterSeries = [new MetricSeries("Mean cell perimeter", new RgbColour(220, 177, 57), perimeters)];
            CellCountSeries = cellCounts;
        }
        catch (Exception exception) when (exception is LaboratoryApiException or HttpRequestException or TaskCanceledException)
        {
            CommandStatus = $"Metrics unavailable: {exception.Message}";
        }
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshAuthoritativeStateAsync(cancellationToken);
                await RefreshMetricsAsync(cancellationToken);
                if (IsTerminal)
                {
                    await DisconnectStreamAsync();
                    await LoadRecordingAsync(cancellationToken);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshAuthoritativeStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            Session = await _apiClient.GetSessionAsync(Session.SessionId, cancellationToken);
            RefreshCommandAvailability();
        }
        catch (Exception exception) when (exception is LaboratoryApiException or HttpRequestException or TaskCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
                CommandStatus = $"Session refresh failed: {exception.Message}";
        }
    }

    private static MetricPoint[] GetPoints(IReadOnlyList<PersistedMetricSampleDto> samples, string key)
    {
        List<MetricPoint> points = new(samples.Count);
        foreach (PersistedMetricSampleDto sample in samples)
        {
            if (sample.Values.TryGetValue(key, out double value) && double.IsFinite(value))
                points.Add(new MetricPoint(sample.Mcs, value));
        }
        return points.ToArray();
    }

    private async Task SelectAtAsync(double x, double y, double viewportWidth, double viewportHeight, CancellationToken cancellationToken)
    {
        if (_displayedFrame is null || viewportWidth <= 0 || viewportHeight <= 0)
            return;
        FullFrameHeader header = _displayedFrame.Header;
        if (!LatticeCoordinates.TryMapViewToCell(x, y, viewportWidth, viewportHeight,
                header.Width, header.Height, Zoom, PanX, PanY, out LatticeCoordinate coordinate))
            return;
        int cellId = _displayedFrame.CellIds[coordinate.Y * header.Width + coordinate.X];
        await Inspector.InspectAsync(cellId, header.Mcs, cancellationToken);
    }

    private void RenderDisplayedFrame()
    {
        if (_displayedFrame is not null)
            Bitmap = _renderer.Render(_displayedFrame, Session.Metadata, ViewMode);
    }

    private void RefreshCommandAvailability()
    {
        OnPropertyChanged(nameof(SessionStatusText));
        OnPropertyChanged(nameof(McsText));
        OnPropertyChanged(nameof(RevisionText));
        OnPropertyChanged(nameof(RecordingStatusText));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanStep));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(IsTerminal));
        StartCommand?.NotifyCanExecuteChanged();
        PauseCommand?.NotifyCanExecuteChanged();
        ResumeCommand?.NotifyCanExecuteChanged();
        StepCommand?.NotifyCanExecuteChanged();
        StopCommand?.NotifyCanExecuteChanged();
    }

    private void UpdateImageTransform() => ImageTransform = new MatrixTransform(new Matrix(Zoom, 0, 0, Zoom, PanX, PanY));

    private (double Scale, double OffsetX, double OffsetY) GetFit(double viewportWidth, double viewportHeight)
    {
        double scale = Math.Min(viewportWidth / Session.Width, viewportHeight / Session.Height);
        double offsetX = (viewportWidth - Session.Width * scale) / 2;
        double offsetY = (viewportHeight - Session.Height * scale) / 2;
        return (scale, offsetX, offsetY);
    }

    partial void OnSessionChanged(SessionDto value) => RefreshCommandAvailability();

    partial void OnZoomChanged(double value) => UpdateImageTransform();
    partial void OnPanXChanged(double value) => UpdateImageTransform();
    partial void OnPanYChanged(double value) => UpdateImageTransform();

    partial void OnViewModeChanged(LatticeViewMode value) => RenderDisplayedFrame();
}
