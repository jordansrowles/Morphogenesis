using System.Diagnostics;
using System.Threading.Channels;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Configuration;
using Rowles.Morphogenesis.Experiments.Execution;
using Rowles.Morphogenesis.Experiments.Results;
using Rowles.Morphogenesis.Laboratory.Publication;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Measurements;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Laboratory.Sessions;

public sealed class SimulationSession : IAsyncDisposable
{
    private const int MaximumRequestsPerBoundary = 64;
    private readonly object _snapshotGate = new();
    private readonly object _disposeGate = new();
    private readonly ExperimentManifest _manifest;
    private readonly ExperimentSimulationInstance _instance;
    private readonly Channel<SessionRequest> _requests;
    private readonly CancellationTokenSource _workerCancellation = new();
    private readonly LatestValuePublisher<SimulationSessionSnapshot> _statePublisher = new(CloneSnapshot);
    private readonly LatestValuePublisher<MeasurementSample> _measurementPublisher = new(CloneMeasurement);
    private readonly FrameBufferPool _frameBufferPool;
    private readonly LatestFrameHub _frameHub;
    private readonly SimulationRecorder? _recorder;
    private readonly long _startedTimestamp;
    private readonly long _minimumPublishIntervalTicks;
    private readonly Task? _workerStartGate;
    private readonly Task _worker;
    private SimulationSessionStatus _status = SimulationSessionStatus.Created;
    private long _revision;
    private long _currentMcs;
    private MeasurementSample? _latestMeasurement;
    private string? _failure;
    private RecordingState _recordingState = RecordingState.Disabled;
    private string? _recordingFailure;
    private long _attempts;
    private long _accepted;
    private long _rejected;
    private long _noOps;
    private long _connectivityFallbacks;
    private long _publishedFrames;
    private long _coalescedOrDroppedFrames;
    private long _frameCaptureTicks;
    private long _capturedFrames;
    private long _frameSequence;
    private long _lastPublishTimestamp;
    private int _disposeStarted;
    private Task? _disposeTask;

    internal SimulationSession(
        Guid sessionId,
        ExperimentManifest manifest,
        ExperimentSimulationInstance instance,
        SimulationSessionOptions options,
        Task? workerStartGate = null,
        RecordingOptions? recordingOptions = null,
        CoalescingLatticeChangeAccumulator? recordingAccumulator = null,
        IRecordingStore? recordingStore = null)
    {
        _manifest = manifest;
        _instance = instance;
        _workerStartGate = workerStartGate;
        RecordingOptions resolvedRecordingOptions = recordingOptions ?? new RecordingOptions();
        if (resolvedRecordingOptions.Enabled)
        {
            _recordingState = RecordingState.Active;
        }
        Metadata = CreateMetadata(sessionId, manifest, instance);
        _startedTimestamp = Stopwatch.GetTimestamp();
        _minimumPublishIntervalTicks = Math.Max(1,
            (long)Math.Ceiling(Stopwatch.Frequency / (double)options.LivePublishMaxFps));
        _requests = Channel.CreateBounded<SessionRequest>(new BoundedChannelOptions(options.CommandQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        _frameBufferPool = new FrameBufferPool(options.FrameBufferCount, checked(manifest.GridWidth * manifest.GridHeight));
        _frameHub = new LatestFrameHub(
            sessionId,
            manifest.GridWidth,
            manifest.GridHeight,
            options.MaxLiveSubscribers,
            IncrementCoalescedOrDroppedFrames);

        if (resolvedRecordingOptions.Enabled)
        {
            _recorder = new SimulationRecorder(
                sessionId,
                manifest,
                Metadata,
                instance.Simulation.State,
                recordingAccumulator ?? throw new ArgumentNullException(nameof(recordingAccumulator)),
                resolvedRecordingOptions,
                recordingStore ?? throw new ArgumentNullException(nameof(recordingStore)),
                SetRecordingState);
        }

        _currentMcs = instance.Simulation.CompletedMcs;
        if (manifest.Measurements.IncludeMcsZero || manifest.McsCount == 0)
        {
            PublishMeasurement(Measure());
        }

        _ = TryCaptureFrame();
        PublishStateSnapshot();
        _lastPublishTimestamp = Stopwatch.GetTimestamp();
        _worker = Task.Run(WorkerLoopAsync);
    }

    public SimulationMetadata Metadata { get; }

    public MeasurementSample? LatestMeasurement
    {
        get
        {
            lock (_snapshotGate)
            {
                return _latestMeasurement is null ? null : CloneMeasurement(_latestMeasurement);
            }
        }
    }

    public SimulationSessionSnapshot GetSnapshot()
    {
        lock (_snapshotGate)
        {
            return CreateSnapshotLocked();
        }
    }

    public IAsyncEnumerable<SimulationSessionSnapshot> WatchStateAsync(
        CancellationToken cancellationToken = default) => _statePublisher.Watch(cancellationToken);

    public IAsyncEnumerable<MeasurementSample> WatchMeasurementsAsync(
        CancellationToken cancellationToken = default) => _measurementPublisher.Watch(cancellationToken);

    public IAsyncEnumerable<SimulationFrameLease> WatchFramesAsync(
        CancellationToken cancellationToken = default) => _frameHub.Watch(cancellationToken);

    public int LiveSubscriberCount => _frameHub.SubscriberCount;

    public ValueTask<SimulationCommandResult> StartAsync(
        Guid commandId,
        long expectedRevision,
        CancellationToken cancellationToken = default) =>
        EnqueueCommandAsync(new SimulationCommand(commandId, expectedRevision, SimulationCommandKind.Start), cancellationToken);

    public ValueTask<SimulationCommandResult> PauseAsync(
        Guid commandId,
        long expectedRevision,
        CancellationToken cancellationToken = default) =>
        EnqueueCommandAsync(new SimulationCommand(commandId, expectedRevision, SimulationCommandKind.Pause), cancellationToken);

    public ValueTask<SimulationCommandResult> ResumeAsync(
        Guid commandId,
        long expectedRevision,
        CancellationToken cancellationToken = default) =>
        EnqueueCommandAsync(new SimulationCommand(commandId, expectedRevision, SimulationCommandKind.Resume), cancellationToken);

    public ValueTask<SimulationCommandResult> StepAsync(
        Guid commandId,
        long expectedRevision,
        CancellationToken cancellationToken = default) =>
        EnqueueCommandAsync(new SimulationCommand(commandId, expectedRevision, SimulationCommandKind.Step), cancellationToken);

    public ValueTask<SimulationCommandResult> StopAsync(
        Guid commandId,
        long expectedRevision,
        CancellationToken cancellationToken = default) =>
        EnqueueCommandAsync(new SimulationCommand(commandId, expectedRevision, SimulationCommandKind.Stop), cancellationToken);

    public ValueTask<SimulationCommandResult> CancelForResourcePolicyAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return EnqueueCommandAsync(new SimulationCommand(
            Guid.NewGuid(),
            GetSnapshot().Revision,
            SimulationCommandKind.Stop,
            $"ResourcePolicy: {reason}"), cancellationToken);
    }

    public ValueTask<CellInspection?> InspectCellAsync(
        int cellId,
        CancellationToken cancellationToken = default) => EnqueueInspectionAsync(cellId, cancellationToken);

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private static SimulationMetadata CreateMetadata(
        Guid sessionId,
        ExperimentManifest manifest,
        ExperimentSimulationInstance instance)
    {
        MorphogenesisState state = instance.Simulation.State;
        int[] cellTypeByCellId = new int[state.CellCapacity];
        Array.Fill(cellTypeByCellId, -1);
        cellTypeByCellId[0] = 0;
        for (int cellId = 1; cellId < state.CellCapacity; cellId++)
        {
            if (state.TryGetCellState(cellId, out CellState cell))
            {
                cellTypeByCellId[cellId] = cell.CellTypeId;
            }
        }

        SimulationRunIdentity runIdentity = new(
            instance.ExperimentId,
            instance.ReplicateId,
            instance.ReplicateIndex,
            instance.ReplicateSeed,
            instance.InitialisationSeed,
            instance.DynamicsSeed,
            SerialSimulation.KernelId);
        return new SimulationMetadata(
            sessionId,
            runIdentity,
            state.Width,
            state.Height,
            state.Configuration.BoundaryMode,
            state.Configuration.Conventions.CopyNeighbourhood,
            state.Configuration.Conventions.ContactCouplingNeighbourhood,
            state.Configuration.Conventions.PerimeterNeighbourhood,
            state.Configuration.Conventions.ConnectivityAdjacency,
            manifest.SchemaVersion,
            manifest.CellTypes,
            cellTypeByCellId);
    }

    private async ValueTask<SimulationCommandResult> EnqueueCommandAsync(
        SimulationCommand command,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        RequestCompletion<SimulationCommandResult> completion = new(cancellationToken);
        try
        {
            await _requests.Writer.WriteAsync(new CommandRequest(command, completion), cancellationToken).ConfigureAwait(false);
            return await completion.Task.ConfigureAwait(false);
        }
        catch (ChannelClosedException exception)
        {
            throw new ObjectDisposedException(nameof(SimulationSession), exception.Message);
        }
        finally
        {
            completion.Dispose();
        }
    }

    private async ValueTask<CellInspection?> EnqueueInspectionAsync(int cellId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        RequestCompletion<CellInspection?> completion = new(cancellationToken);
        try
        {
            await _requests.Writer.WriteAsync(new InspectionRequest(cellId, completion), cancellationToken).ConfigureAwait(false);
            return await completion.Task.ConfigureAwait(false);
        }
        catch (ChannelClosedException exception)
        {
            throw new ObjectDisposedException(nameof(SimulationSession), exception.Message);
        }
        finally
        {
            completion.Dispose();
        }
    }

    private async Task WorkerLoopAsync()
    {
        CancellationToken cancellationToken = _workerCancellation.Token;
        try
        {
            if (_workerStartGate is not null)
            {
                await _workerStartGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                if (GetStatus() == SimulationSessionStatus.Running)
                {
                    if (GetCurrentMcs() >= _manifest.McsCount)
                    {
                        CaptureFinalFrameIfNeeded();
                        TransitionToTerminal(SimulationSessionStatus.Completed, null);
                        ProcessQueuedRequests();
                        continue;
                    }

                    try
                    {
                        ExecuteOneMcs();
                    }
                    catch (Exception exception)
                    {
                        FailSession(exception);
                        ProcessQueuedRequests();
                        continue;
                    }

                    if (GetCurrentMcs() >= _manifest.McsCount)
                    {
                        CaptureFinalFrameIfNeeded();
                        TransitionToTerminal(SimulationSessionStatus.Completed, null);
                        ProcessQueuedRequests();
                        continue;
                    }

                    TryCaptureScheduledFrame();
                    ProcessQueuedRequests();
                    continue;
                }

                SessionRequest request = await _requests.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                ProcessRequest(request);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ChannelClosedException)
        {
        }
        catch (Exception exception)
        {
            FailSession(exception);
            ProcessQueuedRequests();
        }
    }

    private void ProcessQueuedRequests()
    {
        for (int processed = 0;
             processed < MaximumRequestsPerBoundary && _requests.Reader.TryRead(out SessionRequest? request);
             processed++)
        {
            ProcessRequest(request);
        }
    }

    private void ProcessRequest(SessionRequest request)
    {
        if (!request.TryBegin())
        {
            return;
        }

        switch (request)
        {
            case CommandRequest commandRequest:
                try
                {
                    SimulationCommandResult result = ApplyCommand(commandRequest.Command);
                    commandRequest.Completion.SetResult(result);
                }
                catch (Exception exception)
                {
                    FailSession(exception);
                    commandRequest.Completion.SetResult(CreateCommandResult(
                        SimulationCommandDisposition.Applied,
                        null));
                }

                break;
            case InspectionRequest inspectionRequest:
                try
                {
                    inspectionRequest.Completion.SetResult(InspectCell(inspectionRequest.CellId));
                }
                catch (Exception exception)
                {
                    inspectionRequest.Completion.SetException(exception);
                }

                break;
        }
    }

    private SimulationCommandResult ApplyCommand(SimulationCommand command)
    {
        SimulationSessionStatus status;
        long revision;
        lock (_snapshotGate)
        {
            status = _status;
            revision = _revision;
        }

        if (IsTerminal(status))
        {
            return CreateCommandResult(SimulationCommandDisposition.Terminal, null);
        }

        if (command.ExpectedRevision != revision)
        {
            return CreateCommandResult(SimulationCommandDisposition.Conflict, command.ExpectedRevision);
        }

        switch (status, command.Kind)
        {
            case (SimulationSessionStatus.Created, SimulationCommandKind.Start):
                Transition(SimulationSessionStatus.Running, incrementRevision: true);
                return CreateCommandResult(SimulationCommandDisposition.Applied, null);
            case (SimulationSessionStatus.Running, SimulationCommandKind.Pause):
                Transition(SimulationSessionStatus.Paused, incrementRevision: true);
                return CreateCommandResult(SimulationCommandDisposition.Applied, null);
            case (SimulationSessionStatus.Paused, SimulationCommandKind.Resume):
                Transition(SimulationSessionStatus.Running, incrementRevision: true);
                return CreateCommandResult(SimulationCommandDisposition.Applied, null);
            case (SimulationSessionStatus.Created, SimulationCommandKind.Step):
            case (SimulationSessionStatus.Paused, SimulationCommandKind.Step):
                ApplyStep();
                return CreateCommandResult(SimulationCommandDisposition.Applied, null);
            case (SimulationSessionStatus.Created, SimulationCommandKind.Stop):
            case (SimulationSessionStatus.Running, SimulationCommandKind.Stop):
            case (SimulationSessionStatus.Paused, SimulationCommandKind.Stop):
                CaptureFinalFrameIfNeeded();
                TransitionToTerminal(SimulationSessionStatus.Cancelled, command.Failure);
                return CreateCommandResult(SimulationCommandDisposition.Applied, null);
            default:
                return CreateCommandResult(SimulationCommandDisposition.InvalidState, null);
        }
    }

    private void ApplyStep()
    {
        if (GetCurrentMcs() < _manifest.McsCount)
        {
            ExecuteOneMcs();
        }

        CaptureFinalFrameIfNeeded();
        SimulationSessionStatus status = GetCurrentMcs() >= _manifest.McsCount
            ? SimulationSessionStatus.Completed
            : SimulationSessionStatus.Paused;
        Transition(status, incrementRevision: true);
    }

    private void ExecuteOneMcs()
    {
        McsSummary summary = _instance.Simulation.RunMcs();
        long mcs = _instance.Simulation.CompletedMcs;
        lock (_snapshotGate)
        {
            _currentMcs = mcs;
            _attempts += summary.Attempts;
            _accepted += summary.Accepted;
            _rejected += summary.Rejected;
            _noOps += summary.NoOps;
            _connectivityFallbacks += summary.ConnectivityFallbacks;
        }

        if (mcs % _manifest.Measurements.EveryMcs == 0 || mcs == _manifest.McsCount)
        {
            PublishMeasurement(Measure());
        }

        _recorder?.RecordCompletedMcs(mcs);
    }

    private TissueMeasurements Measure() => TissueMeasurementCalculator.Measure(
        _instance.Simulation.State,
        _manifest.Initialiser.TypeAId,
        _manifest.Initialiser.TypeBId,
        _manifest.Measurements.InterfaceNeighbourhood);

    private void PublishMeasurement(TissueMeasurements metrics)
    {
        long mcs = GetCurrentMcs();
        MeasurementSample sample = new(mcs, metrics);
        lock (_snapshotGate)
        {
            _latestMeasurement = CloneMeasurement(sample);
        }

        _measurementPublisher.Publish(sample);
        PublishStateSnapshot();
    }

    private CellInspection? InspectCell(int cellId)
    {
        MorphogenesisState state = _instance.Simulation.State;
        if (!state.TryGetCellState(cellId, out CellState cell))
        {
            return null;
        }

        string? name = null;
        foreach (CellTypeDefinition definition in _manifest.CellTypes)
        {
            if (definition.TypeId == cell.CellTypeId)
            {
                name = definition.Name;
                break;
            }
        }

        if (name is null)
        {
            return null;
        }

        return new CellInspection(
            GetCurrentMcs(),
            cell.CellId,
            cell.CellTypeId,
            name,
            cell.Area,
            cell.Perimeter,
            cell.TargetArea,
            cell.AreaStiffness,
            cell.TargetPerimeter,
            cell.PerimeterStiffness);
    }

    private void TryCaptureScheduledFrame()
    {
        long now = Stopwatch.GetTimestamp();
        if (now - Volatile.Read(ref _lastPublishTimestamp) >= _minimumPublishIntervalTicks && TryCaptureFrame())
        {
            Volatile.Write(ref _lastPublishTimestamp, Stopwatch.GetTimestamp());
        }
    }

    private void CaptureFinalFrameIfNeeded()
    {
        if (_frameHub.LatestMcs != GetCurrentMcs())
        {
            if (TryCaptureFrame())
            {
                Volatile.Write(ref _lastPublishTimestamp, Stopwatch.GetTimestamp());
            }
        }
    }

    private bool TryCaptureFrame()
    {
        if (!_frameBufferPool.TryAcquire(out FrameBufferOwner? owner) || owner is null)
        {
            IncrementCoalescedOrDroppedFrames();
            return false;
        }

        long started = Stopwatch.GetTimestamp();
        try
        {
            _instance.Simulation.State.CopyCellIdsTo(owner.CellIds);
            Interlocked.Increment(ref _capturedFrames);
        }
        catch
        {
            owner.Release();
            throw;
        }

        Interlocked.Add(ref _frameCaptureTicks, Stopwatch.GetTimestamp() - started);
        long sequence = ++_frameSequence;
        try
        {
            if (!_frameHub.Publish(owner, sequence, GetCurrentMcs()))
            {
                return false;
            }

            Interlocked.Increment(ref _publishedFrames);
            return true;
        }
        finally
        {
            owner.Release();
        }
    }

    private void IncrementCoalescedOrDroppedFrames() =>
        Interlocked.Increment(ref _coalescedOrDroppedFrames);

    private void Transition(SimulationSessionStatus status, bool incrementRevision)
    {
        lock (_snapshotGate)
        {
            _status = status;
            if (incrementRevision)
            {
                _revision = checked(_revision + 1);
            }
        }

        PublishStateSnapshot();
        if (IsTerminal(status))
        {
            CompleteRecordingAtCurrentMcs();
        }
    }

    private void TransitionToTerminal(SimulationSessionStatus status, string? failure)
    {
        lock (_snapshotGate)
        {
            if (IsTerminal(_status))
            {
                return;
            }

            _status = status;
            _failure = failure;
            _revision = checked(_revision + 1);
        }

        PublishStateSnapshot();
        CompleteRecordingAtCurrentMcs();
    }

    private void FailSession(Exception exception)
    {
        CaptureFinalFrameIfNeeded();
        TransitionToTerminal(
            SimulationSessionStatus.Failed,
            $"{exception.GetType().Name}: {exception.Message}");
    }

    private void PublishStateSnapshot() => _statePublisher.Publish(GetSnapshot());

    private void SetRecordingState(RecordingState state, string? failure)
    {
        lock (_snapshotGate)
        {
            _recordingState = state;
            _recordingFailure = failure;
        }

        PublishStateSnapshot();
    }

    private void CompleteRecordingAtCurrentMcs()
    {
        SimulationRecorder? recorder = _recorder;
        if (recorder is not null)
        {
            _ = recorder.CompleteAsync(GetCurrentMcs());
        }
    }

    private SimulationSessionSnapshot CreateSnapshotLocked() => new(
        Metadata.SessionId,
        _revision,
        _status,
        _currentMcs,
        _latestMeasurement is null ? null : CloneMeasurement(_latestMeasurement),
        CreateCountersLocked(),
        _failure,
        _recordingState,
        _recordingFailure);

    private SimulationOperationalCounters CreateCountersLocked() => new(
        Stopwatch.GetElapsedTime(_startedTimestamp),
        _currentMcs,
        _attempts,
        _accepted,
        _rejected,
        _noOps,
        _connectivityFallbacks,
        Interlocked.Read(ref _publishedFrames),
        Interlocked.Read(ref _coalescedOrDroppedFrames),
        Interlocked.Read(ref _frameCaptureTicks),
        Interlocked.Read(ref _capturedFrames));

    private SimulationCommandResult CreateCommandResult(
        SimulationCommandDisposition disposition,
        long? expectedRevision)
    {
        lock (_snapshotGate)
        {
            return new SimulationCommandResult(
                Metadata.SessionId,
                _revision,
                _status,
                _currentMcs,
                disposition,
                expectedRevision);
        }
    }

    private SimulationSessionStatus GetStatus()
    {
        lock (_snapshotGate)
        {
            return _status;
        }
    }

    private long GetCurrentMcs()
    {
        lock (_snapshotGate)
        {
            return _currentMcs;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposeStarted, 1);
        _requests.Writer.TryComplete();
        _workerCancellation.Cancel();
        try
        {
            await _worker.ConfigureAwait(false);
        }
        finally
        {
            if (_recorder is not null)
            {
                await _recorder.CompleteAsync(GetCurrentMcs()).ConfigureAwait(false);
                await _recorder.DisposeAsync().ConfigureAwait(false);
            }

            ObjectDisposedException disposedException = new(nameof(SimulationSession));
            while (_requests.Reader.TryRead(out SessionRequest? request))
            {
                request.FailIfPending(disposedException);
            }

            _frameHub.Dispose();
            _frameBufferPool.Dispose();
            _statePublisher.Dispose();
            _measurementPublisher.Dispose();
            _workerCancellation.Dispose();
        }
    }

    private static MeasurementSample CloneMeasurement(MeasurementSample sample) => sample with
    {
        Metrics = sample.Metrics with
        {
            HomotypicInterfacesByType = sample.Metrics.HomotypicInterfacesByType.ToArray(),
            DomainsByType = sample.Metrics.DomainsByType.ToArray()
        }
    };

    private static SimulationSessionSnapshot CloneSnapshot(SimulationSessionSnapshot snapshot) => snapshot with
    {
        LatestMeasurement = snapshot.LatestMeasurement is null ? null : CloneMeasurement(snapshot.LatestMeasurement)
    };

    private static bool IsTerminal(SimulationSessionStatus status) => status is
        SimulationSessionStatus.Completed or SimulationSessionStatus.Failed or
        SimulationSessionStatus.Interrupted or SimulationSessionStatus.Cancelled;

    private abstract class SessionRequest
    {
        internal abstract bool TryBegin();

        internal abstract void FailIfPending(Exception exception);
    }

    private sealed class CommandRequest(
        SimulationCommand command,
        RequestCompletion<SimulationCommandResult> completion) : SessionRequest
    {
        internal SimulationCommand Command { get; } = command;

        internal RequestCompletion<SimulationCommandResult> Completion { get; } = completion;

        internal override bool TryBegin() => Completion.TryBegin();

        internal override void FailIfPending(Exception exception) => Completion.FailIfPending(exception);
    }

    private sealed class InspectionRequest(
        int cellId,
        RequestCompletion<CellInspection?> completion) : SessionRequest
    {
        internal int CellId { get; } = cellId;

        internal RequestCompletion<CellInspection?> Completion { get; } = completion;

        internal override bool TryBegin() => Completion.TryBegin();

        internal override void FailIfPending(Exception exception) => Completion.FailIfPending(exception);
    }

    private sealed class RequestCompletion<T> : IDisposable
    {
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<T> _source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _cancellationRegistration;
        private int _state;

        internal RequestCompletion(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            if (cancellationToken.CanBeCanceled)
            {
                _cancellationRegistration = cancellationToken.Register(
                    static state => ((RequestCompletion<T>)state!).CancelBeforeStart(),
                    this);
            }
        }

        internal Task<T> Task => _source.Task;

        internal bool TryBegin() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

        internal void SetResult(T result)
        {
            if (Interlocked.CompareExchange(ref _state, 3, 1) == 1)
            {
                _source.TrySetResult(result);
            }
        }

        internal void SetException(Exception exception)
        {
            if (Interlocked.CompareExchange(ref _state, 3, 1) == 1)
            {
                _source.TrySetException(exception);
            }
        }

        internal void FailIfPending(Exception exception)
        {
            if (Interlocked.CompareExchange(ref _state, 3, 0) == 0)
            {
                _source.TrySetException(exception);
            }
        }

        public void Dispose() => _cancellationRegistration.Dispose();

        private void CancelBeforeStart()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            {
                _source.TrySetCanceled(_cancellationToken);
            }
        }
    }
}
