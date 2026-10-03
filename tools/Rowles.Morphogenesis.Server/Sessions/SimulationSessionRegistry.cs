using System.Collections.Concurrent;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Results;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Server.Configuration;
using Rowles.Morphogenesis.Server.Diagnostics;
using Rowles.Morphogenesis.Server.Experiments;
using Rowles.Morphogenesis.Server.Persistence;

namespace Rowles.Morphogenesis.Server.Sessions;

public sealed class SimulationSessionRegistry : IHostedService, IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, ActiveSession> _sessions = new();
    private readonly ExperimentCatalog _catalog;
    private readonly LaboratoryDatabase _database;
    private readonly SqliteRecordingStore _recordingStore;
    private readonly LaboratoryResourceLimits _limits;
    private readonly SessionCapacityService _capacity;
    private readonly LaboratoryDiagnostics _diagnostics;
    private readonly ILogger<SimulationSessionRegistry> _logger;
    private readonly SemaphoreSlim _policyGate = new(1, 1);
    private readonly SemaphoreSlim _creationGate = new(1, 1);
    private readonly object _disposeTaskGate = new();
    private readonly CancellationTokenSource _stopping = new();
    private Task? _disposeTask;
    private int _disposed;

    public SimulationSessionRegistry(
        ExperimentCatalog catalog,
        LaboratoryDatabase database,
        SqliteRecordingStore recordingStore,
        LaboratoryResourceLimits limits,
        SessionCapacityService capacity,
        LaboratoryDiagnostics diagnostics,
        ILogger<SimulationSessionRegistry> logger)
    {
        _catalog = catalog;
        _database = database;
        _recordingStore = recordingStore;
        _limits = limits;
        _capacity = capacity;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Terminal writes must finish before the host lifecycle service closes SQLite.
        await DisposeSessionsAsync().ConfigureAwait(false);
    }

    public bool TryGetSession(Guid sessionId, out SimulationSession session)
    {
        if (_sessions.TryGetValue(sessionId, out ActiveSession? active))
        {
            session = active.Session;
            return true;
        }

        session = null!;
        return false;
    }

    public IReadOnlyList<SimulationSession> GetLiveSessions() =>
        _sessions.Values.Select(active => active.Session).ToArray();

    public async Task<SessionDto> CreateSessionAsync(
        CreateSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ExperimentId) || !_catalog.TryGet(request.ExperimentId, out ExperimentCatalogEntry entry))
            throw new SessionRequestException("The requested canonical experiment does not exist.");
        if (request.ReplicateIndex < 0 || request.ReplicateIndex >= entry.ReplicateCount)
            throw new SessionRequestException("The replicate index is outside the experiment manifest.");
        try
        {
            _limits.GetSiteCount(entry.GridWidth, entry.GridHeight);
        }
        catch (LaboratoryResourceLimitException exception)
        {
            _diagnostics.RecordCommandFailure();
            throw new SessionRequestException(exception.Message);
        }
        if (request.LivePublishMaxFps < 1 || request.LivePublishMaxFps > _limits.MaxLivePublishFps)
            throw new SessionRequestException($"Live publish FPS must be between 1 and {_limits.MaxLivePublishFps}.");

        if (!_capacity.TryReserveResident(out IDisposable? residentReservation) || residentReservation is null)
            throw new SessionCapacityException("The maximum number of resident simulation sessions has been reached.");

        ExperimentManifest? manifest = null;
        RecordingOptions recordingOptions = new() { Enabled = request.RecordingEnabled };
        SimulationSession? session = null;
        ActiveSession? active = null;
        bool creationGateAcquired = false;
        try
        {
            await _creationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            creationGateAcquired = true;
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            manifest = _catalog.GetManifest(entry);
            session = SimulationSessionFactory.Create(
                manifest,
                request.ReplicateIndex,
                new SimulationSessionOptions
                {
                    LivePublishMaxFps = request.LivePublishMaxFps,
                    CommandQueueCapacity = _limits.CommandQueueCapacity,
                    MaxLiveSubscribers = _limits.MaxLiveSubscribersPerSession,
                    FrameBufferCount = _limits.FrameBufferCountPerSession
                },
                recordingOptions with { WriterQueueCapacity = _limits.RecordingWriterQueueCapacity },
                request.RecordingEnabled ? _recordingStore : null);
            active = new ActiveSession(session, manifest, DateTimeOffset.UtcNow, residentReservation);
            residentReservation = null;

            if (request.RecordingEnabled)
            {
                await _recordingStore.WaitForRunCreationAsync(session.Metadata.SessionId, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await _database.CreateRunAsync(
                    manifest,
                    session.Metadata,
                    session.GetSnapshot(),
                    RecordingSettings.Disabled,
                    active.CreatedAtUtc,
                    cancellationToken).ConfigureAwait(false);
            }

            if (!_sessions.TryAdd(session.Metadata.SessionId, active))
                throw new InvalidOperationException("A session identifier was already registered.");

            active.StartObservers(_stopping.Token, _database, _capacity, _diagnostics, _logger, RetireObservedTerminalSessionAsync);
            await PersistSnapshotAsync(active, session.GetSnapshot().LatestMeasurement, CancellationToken.None).ConfigureAwait(false);
            if (request.RecordingEnabled)
            {
                _logger.LogInformation(
                    "Recording started for session {SessionId}, experiment {ExperimentId}, replicate {ReplicateId}, MCS {Mcs}, revision {Revision}",
                    session.Metadata.SessionId,
                    session.Metadata.RunIdentity.ExperimentId,
                    session.Metadata.RunIdentity.ReplicateId,
                    session.GetSnapshot().CurrentMcs,
                    session.GetSnapshot().Revision);
            }
            return await GetDtoAsync(active, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            if (active is not null && session is not null && _sessions.TryRemove(session.Metadata.SessionId, out _))
            {
                _capacity.RemoveSession(session.Metadata.SessionId);
                await active.DisposeAsync().ConfigureAwait(false);
            }
            else if (active is not null)
            {
                await active.DisposeAsync().ConfigureAwait(false);
            }
            else if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
        finally
        {
            if (creationGateAcquired)
                _creationGate.Release();
            residentReservation?.Dispose();
        }
    }

    public async Task<SessionDto?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        PersistedRun? run = await _database.GetRunAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (run is null)
            return null;
        return _sessions.TryGetValue(sessionId, out ActiveSession? active)
            ? SessionDto.From(run, active.Session.GetSnapshot(), active.StartedAtUtc, active.CompletedAtUtc)
            : SessionDto.From(run);
    }

    public async Task<IReadOnlyList<SessionDto>> GetSessionsAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PersistedRun> runs = await _database.GetRunsAsync(take, cancellationToken).ConfigureAwait(false);
        return runs.Select(run => _sessions.TryGetValue(run.Id, out ActiveSession? active)
                ? SessionDto.From(run, active.Session.GetSnapshot(), active.StartedAtUtc, active.CompletedAtUtc)
                : SessionDto.From(run))
            .ToArray();
    }

    public async Task<SessionDto?> GetAuthoritativeDtoAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out ActiveSession? active))
            return await GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return await GetDtoAsync(active, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SimulationCommandResult> ExecuteCommandAsync(
        Guid sessionId,
        SimulationCommandKind kind,
        Guid commandId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        await _policyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(sessionId, out ActiveSession? active))
                throw new KeyNotFoundException($"Live session '{sessionId:D}' was not found.");

            SimulationSessionStatus status = active.Session.GetSnapshot().Status;
            bool reservesRunning = kind is SimulationCommandKind.Start or SimulationCommandKind.Resume;
            bool reservesStep = kind == SimulationCommandKind.Step &&
                status is SimulationSessionStatus.Created or SimulationSessionStatus.Paused;
            if (reservesRunning && !_capacity.TryReserveRunning(sessionId))
                throw new SessionCapacityException("The maximum number of running simulations has been reached.");
            if (reservesStep && !_capacity.TryReserveStep(sessionId))
                throw new SessionCapacityException("No transient simulation slot is available for this step.");

            try
            {
                SimulationCommandResult result = kind switch
                {
                    SimulationCommandKind.Start => await active.Session.StartAsync(commandId, expectedRevision, cancellationToken).ConfigureAwait(false),
                    SimulationCommandKind.Pause => await active.Session.PauseAsync(commandId, expectedRevision, cancellationToken).ConfigureAwait(false),
                    SimulationCommandKind.Resume => await active.Session.ResumeAsync(commandId, expectedRevision, cancellationToken).ConfigureAwait(false),
                    SimulationCommandKind.Step => await active.Session.StepAsync(commandId, expectedRevision, cancellationToken).ConfigureAwait(false),
                    SimulationCommandKind.Stop => await active.Session.StopAsync(commandId, expectedRevision, cancellationToken).ConfigureAwait(false),
                    _ => throw new ArgumentOutOfRangeException(nameof(kind))
                };

                SimulationSessionSnapshot after = active.Session.GetSnapshot();
                _capacity.ObserveStatus(sessionId, after.Status);
                if (result.Disposition == SimulationCommandDisposition.Applied)
                {
                    active.MarkActivity(DateTimeOffset.UtcNow);
                    _logger.LogInformation(
                        "Applied {CommandKind} to simulation session {SessionId} at MCS {Mcs} revision {Revision}",
                        kind,
                        sessionId,
                        after.CurrentMcs,
                        after.Revision);
                    if (kind is SimulationCommandKind.Pause or SimulationCommandKind.Stop)
                        _capacity.ReleaseRunning(sessionId);
                    if (kind == SimulationCommandKind.Pause)
                        await EnforcePausedSessionLimitAsync(sessionId, cancellationToken).ConfigureAwait(false);
                }
                else if (result.Disposition == SimulationCommandDisposition.Conflict)
                {
                    _diagnostics.RecordCommandConflict();
                    _logger.LogWarning(
                        "Rejected stale {CommandKind} for simulation session {SessionId}: expected revision {ExpectedRevision}, current revision {CurrentRevision}",
                        kind,
                        sessionId,
                        expectedRevision,
                        result.Revision);
                }
                else if (result.Disposition == SimulationCommandDisposition.Failed)
                {
                    _diagnostics.RecordCommandFailure();
                    _logger.LogError(
                        "{CommandKind} failed while applying to simulation session {SessionId} at MCS {Mcs}, revision {Revision}: {Failure}",
                        kind,
                        sessionId,
                        result.CurrentMcs,
                        result.Revision,
                        result.Failure);
                }
                else
                {
                    _diagnostics.RecordCommandFailure();
                    _logger.LogWarning(
                        "Rejected invalid {CommandKind} for simulation session {SessionId} in status {Status}",
                        kind,
                        sessionId,
                        result.Status);
                }

                return result;
            }
            catch
            {
                SimulationSessionStatus currentStatus = active.Session.GetSnapshot().Status;
                if (currentStatus != SimulationSessionStatus.Running)
                    _capacity.ReleaseRunning(sessionId);
                if (reservesStep)
                    _capacity.CompleteStep(sessionId);
                _diagnostics.RecordCommandFailure();
                throw;
            }
            finally
            {
                if (reservesStep)
                    _capacity.CompleteStep(sessionId);
                if (reservesRunning && active.Session.GetSnapshot().Status != SimulationSessionStatus.Running)
                    _capacity.ReleaseRunning(sessionId);
            }
        }
        finally
        {
            _policyGate.Release();
        }
    }

    public void MarkActivity(Guid sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out ActiveSession? active))
            active.MarkActivity(DateTimeOffset.UtcNow);
    }

    public async Task<int> CancelIdleSessionsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        await _policyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveSession[] idle = _sessions.Values
                .Where(active => IsIdle(active, nowUtc))
                .OrderBy(active => active.LastActivityUtc)
                .ToArray();
            int cancelled = 0;
            foreach (ActiveSession active in idle)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SimulationSessionSnapshot snapshot = active.Session.GetSnapshot();
                string reason = snapshot.Status == SimulationSessionStatus.Created
                    ? "CreatedSessionIdleTimeout"
                    : "PausedSessionIdleTimeout";
                SimulationCommandResult result = await active.Session.CancelForResourcePolicyAsync(reason, cancellationToken).ConfigureAwait(false);
                if (result.Disposition != SimulationCommandDisposition.Applied)
                    continue;

                await RemoveTerminalSessionAsync(active).ConfigureAwait(false);
                _logger.LogInformation(
                    "Cancelled idle simulation session {SessionId} under resource policy {Policy}",
                    snapshot.SessionId,
                    reason);
                cancelled++;
            }

            return cancelled;
        }
        finally
        {
            _policyGate.Release();
        }
    }

    public async Task RemovePersistedSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await _policyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessions.TryGetValue(sessionId, out ActiveSession? active))
            {
                await active.StopObserversAsync().ConfigureAwait(false);
                _diagnostics.RecordRetiredSession(active.Session.GetSnapshot());
                _sessions.TryRemove(sessionId, out _);
                _capacity.RemoveSession(sessionId);
                await active.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _policyGate.Release();
        }
    }

    public async Task<IReadOnlyList<PersistedMetricSample>> GetMetricsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PersistedMetricSample> persisted = await _database.GetMetricsAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (!_sessions.TryGetValue(sessionId, out ActiveSession? active))
            return persisted;

        MeasurementSample? latest = active.Session.GetSnapshot().LatestMeasurement;
        if (latest is null)
            return persisted;
        Dictionary<long, PersistedMetricSample> byMcs = persisted.ToDictionary(sample => sample.Mcs);
        byMcs[latest.Mcs] = MetricRowMapper.ToSample(latest);
        return byMcs.Values.OrderBy(sample => sample.Mcs).ToArray();
    }

    internal async Task<bool> HasRunAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        await _database.GetRunAsync(sessionId, cancellationToken).ConfigureAwait(false) is not null;

    private async Task<SessionDto> GetDtoAsync(ActiveSession active, CancellationToken cancellationToken)
    {
        PersistedRun run = await _database.GetRunAsync(active.Session.Metadata.SessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The session has not been persisted.");
        return SessionDto.From(run, active.Session.GetSnapshot(), active.StartedAtUtc, active.CompletedAtUtc);
    }

    private async Task PersistSnapshotAsync(
        ActiveSession active,
        MeasurementSample? measurement,
        CancellationToken cancellationToken)
    {
        await active.PersistenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SimulationSessionSnapshot snapshot = active.Session.GetSnapshot();
            await _database.UpdateSnapshotAsync(
                snapshot.SessionId,
                snapshot,
                measurement,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            active.MarkPersisted(snapshot);
        }
        finally
        {
            active.PersistenceGate.Release();
        }
    }

    private async Task EnforcePausedSessionLimitAsync(Guid justPausedSessionId, CancellationToken cancellationToken)
    {
        ActiveSession[] paused = _sessions.Values
            .Where(active => active.Session.GetSnapshot().Status == SimulationSessionStatus.Paused)
            .OrderBy(active => active.LastActivityUtc)
            .ToArray();
        int excess = paused.Length - _limits.MaxPausedSessions;
        for (int index = 0; index < excess; index++)
        {
            ActiveSession? oldestIdle = paused.FirstOrDefault(active =>
                active.Session.Metadata.SessionId != justPausedSessionId &&
                _sessions.ContainsKey(active.Session.Metadata.SessionId));
            if (oldestIdle is null)
                break;

            SimulationSessionSnapshot snapshot = oldestIdle.Session.GetSnapshot();
            SimulationCommandResult result = await oldestIdle.Session.CancelForResourcePolicyAsync(
                "MaximumPausedSessionsExceeded",
                cancellationToken).ConfigureAwait(false);
            if (result.Disposition != SimulationCommandDisposition.Applied)
                continue;

            await RemoveTerminalSessionAsync(oldestIdle).ConfigureAwait(false);
            _logger.LogWarning(
                "Cancelled paused simulation session {SessionId} because the paused-session capacity was exceeded; MCS {Mcs}, revision {Revision}",
                snapshot.SessionId,
                snapshot.CurrentMcs,
                snapshot.Revision);
        }
    }

    private async Task RemoveTerminalSessionAsync(ActiveSession active)
    {
        Guid sessionId = active.Session.Metadata.SessionId;
        await active.Session.WaitForRecordingCompletionAsync().ConfigureAwait(false);
        await active.StopObserversAsync().ConfigureAwait(false);
        SimulationSessionSnapshot snapshot = active.Session.GetSnapshot();
        await PersistTerminalStateAsync(active).ConfigureAwait(false);

        _diagnostics.RecordRetiredSession(snapshot);
        _capacity.ObserveStatus(sessionId, snapshot.Status);
        _sessions.TryRemove(sessionId, out _);
        _capacity.RemoveSession(sessionId);
        await active.DisposeAsync().ConfigureAwait(false);
    }

    private async Task PersistTerminalStateAsync(ActiveSession active)
    {
        Guid sessionId = active.Session.Metadata.SessionId;
        while (true)
        {
            try
            {
                SimulationSessionSnapshot snapshot = active.Session.GetSnapshot();
                await PersistSnapshotAsync(active, snapshot.LatestMeasurement, CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch (KeyNotFoundException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Persisted history for terminal simulation session {SessionId} was removed; releasing its live owner",
                    sessionId);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _diagnostics.RecordPersistenceFailure();
                _logger.LogError(
                    exception,
                    "Could not persist terminal state for simulation session {SessionId}; retrying before releasing its resident resources",
                    sessionId);
                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task RetireObservedTerminalSessionAsync(ActiveSession active)
    {
        await _policyGate.WaitAsync(_stopping.Token).ConfigureAwait(false);
        try
        {
            Guid sessionId = active.Session.Metadata.SessionId;
            if (_sessions.TryGetValue(sessionId, out ActiveSession? registered) &&
                ReferenceEquals(active, registered) &&
                IsTerminal(active.Session.GetSnapshot().Status))
            {
                await RemoveTerminalSessionAsync(active).ConfigureAwait(false);
            }
        }
        finally
        {
            _policyGate.Release();
        }
    }

    private bool IsIdle(ActiveSession active, DateTimeOffset nowUtc)
    {
        SimulationSessionSnapshot snapshot = active.Session.GetSnapshot();
        TimeSpan idle = nowUtc - active.LastActivityUtc;
        return snapshot.Status switch
        {
            SimulationSessionStatus.Created => idle >= _limits.CreatedSessionIdleTimeout,
            SimulationSessionStatus.Paused => idle >= _limits.PausedSessionIdleTimeout,
            _ => false
        };
    }

    private static bool IsTerminal(SimulationSessionStatus status) => status is
        SimulationSessionStatus.Completed or SimulationSessionStatus.Failed or
        SimulationSessionStatus.Interrupted or SimulationSessionStatus.Cancelled;

    private Task DisposeSessionsAsync()
    {
        lock (_disposeTaskGate)
        {
            if (_disposeTask is not null)
                return _disposeTask;

            Interlocked.Exchange(ref _disposed, 1);
            _disposeTask = DisposeSessionsCoreAsync();
            return _disposeTask;
        }
    }

    private async Task DisposeSessionsCoreAsync()
    {
        await _creationGate.WaitAsync().ConfigureAwait(false);
        await _policyGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _stopping.Cancel();
            ActiveSession[] sessions = _sessions.Values.ToArray();
            await Task.WhenAll(sessions.Select(active => active.StopObserversAsync(waitForTerminalMonitor: true))).ConfigureAwait(false);
            foreach (ActiveSession active in sessions)
            {
                if (IsTerminal(active.Session.GetSnapshot().Status))
                {
                    await active.Session.WaitForRecordingCompletionAsync().ConfigureAwait(false);
                    await PersistTerminalStateAsync(active).ConfigureAwait(false);
                }

                await active.DisposeAsync().ConfigureAwait(false);
                _capacity.RemoveSession(active.Session.Metadata.SessionId);
            }
            _sessions.Clear();
        }
        finally
        {
            _policyGate.Release();
            _creationGate.Release();
            _stopping.Dispose();
            _policyGate.Dispose();
        }
    }

    public ValueTask DisposeAsync() => new(DisposeSessionsAsync());

    private sealed class ActiveSession : IAsyncDisposable
    {
        private readonly CancellationTokenSource _observerCancellation = new();
        private readonly IDisposable _residentReservation;
        private readonly object _timeGate = new();
        private long _lastProgressMcs;
        private long _lastPersistedRevision = -1;
        private long _lastActivityUtcTicks;
        private SimulationSessionStatus _observedStatus;
        private RecordingState _observedRecordingState;
        private SimulationSessionStatus _lastPersistedStatus;
        private RecordingState _lastPersistedRecordingState;
        private string? _lastPersistedRecordingFailure;
        private Task[] _observers = [];
        private Task _terminalMonitor = Task.CompletedTask;
        private ILogger<SimulationSessionRegistry>? _logger;

        internal ActiveSession(
            SimulationSession session,
            ExperimentManifest manifest,
            DateTimeOffset createdAtUtc,
            IDisposable residentReservation)
        {
            Session = session;
            Manifest = manifest;
            CreatedAtUtc = createdAtUtc;
            _residentReservation = residentReservation;
            _lastActivityUtcTicks = createdAtUtc.UtcTicks;
            SimulationSessionSnapshot snapshot = session.GetSnapshot();
            _observedStatus = snapshot.Status;
            _observedRecordingState = snapshot.RecordingState;
            _lastPersistedRecordingState = snapshot.RecordingState;
            _lastPersistedRecordingFailure = snapshot.RecordingFailure;
        }

        internal SimulationSession Session { get; }
        internal ExperimentManifest Manifest { get; }
        internal DateTimeOffset CreatedAtUtc { get; }
        internal SemaphoreSlim PersistenceGate { get; } = new(1, 1);
        internal DateTimeOffset? StartedAtUtc { get; private set; }
        internal DateTimeOffset? CompletedAtUtc { get; private set; }
        internal DateTimeOffset LastActivityUtc => new(Interlocked.Read(ref _lastActivityUtcTicks), TimeSpan.Zero);

        internal void MarkActivity(DateTimeOffset activityUtc) =>
            Interlocked.Exchange(ref _lastActivityUtcTicks, activityUtc.UtcTicks);

        internal void StartObservers(
            CancellationToken registryStopping,
            LaboratoryDatabase database,
            SessionCapacityService capacity,
            LaboratoryDiagnostics diagnostics,
            ILogger<SimulationSessionRegistry> logger,
            Func<ActiveSession, Task> retireTerminalSession)
        {
            _logger = logger;
            CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(registryStopping, _observerCancellation.Token);
            _observers =
            [
                SuperviseAsync("state persistence", token => ObserveStateAsync(token, database, capacity, logger), linked.Token, diagnostics, logger),
                SuperviseAsync("measurement persistence", token => ObserveMeasurementsAsync(token, database), linked.Token, diagnostics, logger),
                SuperviseAsync("progress persistence", token => PersistProgressAsync(token, database), linked.Token, diagnostics, logger)
            ];
            _terminalMonitor = SuperviseAsync(
                "terminal session retirement",
                token => WatchForTerminalSessionAsync(token, retireTerminalSession),
                linked.Token,
                diagnostics,
                logger);
            _ = Task.WhenAll(_observers.Append(_terminalMonitor)).ContinueWith(
                static (_, state) => ((CancellationTokenSource)state!).Dispose(),
                linked,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        internal void MarkPersisted(SimulationSessionSnapshot snapshot)
        {
            _lastProgressMcs = Math.Max(_lastProgressMcs, snapshot.CurrentMcs);
            _lastPersistedRevision = Math.Max(_lastPersistedRevision, snapshot.Revision);
            _lastPersistedStatus = snapshot.Status;
            _lastPersistedRecordingState = snapshot.RecordingState;
            _lastPersistedRecordingFailure = snapshot.RecordingFailure;
            lock (_timeGate)
            {
                if (snapshot.Status == SimulationSessionStatus.Running)
                    StartedAtUtc ??= DateTimeOffset.UtcNow;
                if (IsTerminal(snapshot.Status))
                    CompletedAtUtc ??= DateTimeOffset.UtcNow;
            }
        }

        internal async Task StopObserversAsync(bool waitForTerminalMonitor = false)
        {
            _observerCancellation.Cancel();
            try
            {
                IEnumerable<Task> observers = waitForTerminalMonitor
                    ? _observers.Append(_terminalMonitor)
                    : _observers;
                await Task.WhenAll(observers).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger?.LogError(exception, "A persistence observer failed while stopping simulation session {SessionId}", Session.Metadata.SessionId);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopObserversAsync().ConfigureAwait(false);
            try
            {
                await Session.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _residentReservation.Dispose();
                _observerCancellation.Dispose();
                PersistenceGate.Dispose();
            }
        }

        private async Task WatchForTerminalSessionAsync(
            CancellationToken cancellationToken,
            Func<ActiveSession, Task> retireTerminalSession)
        {
            await foreach (SimulationSessionSnapshot snapshot in Session.WatchStateAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!IsTerminal(snapshot.Status))
                    continue;

                await Session.WaitForRecordingCompletionAsync().ConfigureAwait(false);
                await retireTerminalSession(this).ConfigureAwait(false);
                return;
            }
        }

        private async Task SuperviseAsync(
            string observerName,
            Func<CancellationToken, Task> observer,
            CancellationToken cancellationToken,
            LaboratoryDiagnostics diagnostics,
            ILogger<SimulationSessionRegistry> logger)
        {
            TimeSpan retryDelay = TimeSpan.FromMilliseconds(100);
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await observer(cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    diagnostics.RecordPersistenceFailure();
                    logger.LogError(
                        exception,
                        "The {ObserverName} observer failed for simulation session {SessionId}; retrying after {RetryDelay}",
                        observerName,
                        Session.Metadata.SessionId,
                        retryDelay);
                    try
                    {
                        await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    retryDelay = TimeSpan.FromMilliseconds(Math.Min(retryDelay.TotalMilliseconds * 2, 5_000));
                }
            }
        }

        private async Task ObserveStateAsync(
            CancellationToken cancellationToken,
            LaboratoryDatabase database,
            SessionCapacityService capacity,
            ILogger<SimulationSessionRegistry> logger)
        {
            await foreach (SimulationSessionSnapshot snapshot in Session.WatchStateAsync(cancellationToken).ConfigureAwait(false))
            {
                capacity.ObserveStatus(snapshot.SessionId, snapshot.Status);
                if (snapshot.Status != _observedStatus)
                {
                    logger.LogInformation(
                        "Simulation session {SessionId} changed to {Status} at MCS {Mcs}, revision {Revision}",
                        snapshot.SessionId,
                        snapshot.Status,
                        snapshot.CurrentMcs,
                        snapshot.Revision);
                    _observedStatus = snapshot.Status;
                }

                if (snapshot.RecordingState != _observedRecordingState)
                {
                    if (snapshot.RecordingState == RecordingState.Failed)
                    {
                        logger.LogWarning(
                            "Recording failed for session {SessionId} at MCS {Mcs}, revision {Revision}: {RecordingFailure}",
                            snapshot.SessionId,
                            snapshot.CurrentMcs,
                            snapshot.Revision,
                            snapshot.RecordingFailure);
                    }
                    else if (snapshot.RecordingState is RecordingState.Active or RecordingState.Completed)
                    {
                        logger.LogInformation(
                            "Recording for session {SessionId} changed to {RecordingState} at MCS {Mcs}, revision {Revision}",
                            snapshot.SessionId,
                            snapshot.RecordingState,
                            snapshot.CurrentMcs,
                            snapshot.Revision);
                    }

                    _observedRecordingState = snapshot.RecordingState;
                }

                if (snapshot.Revision != _lastPersistedRevision || snapshot.Status != _lastPersistedStatus ||
                    snapshot.RecordingState != _lastPersistedRecordingState ||
                    !StringComparer.Ordinal.Equals(snapshot.RecordingFailure, _lastPersistedRecordingFailure))
                    await PersistObservedSnapshotAsync(database, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task ObserveMeasurementsAsync(CancellationToken cancellationToken, LaboratoryDatabase database)
        {
            await foreach (MeasurementSample sample in Session.WatchMeasurementsAsync(cancellationToken).ConfigureAwait(false))
                await PersistObservedSnapshotAsync(database, cancellationToken, sample).ConfigureAwait(false);
        }

        private async Task PersistProgressAsync(CancellationToken cancellationToken, LaboratoryDatabase database)
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                SimulationSessionSnapshot snapshot = Session.GetSnapshot();
                if (snapshot.CurrentMcs >= _lastProgressMcs + 25 && snapshot.Status == SimulationSessionStatus.Running)
                    await PersistObservedSnapshotAsync(database, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task PersistObservedSnapshotAsync(
            LaboratoryDatabase database,
            CancellationToken cancellationToken,
            MeasurementSample? measurement = null)
        {
            await PersistenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                SimulationSessionSnapshot snapshot = Session.GetSnapshot();
                await database.UpdateSnapshotAsync(snapshot.SessionId, snapshot, measurement, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
                MarkPersisted(snapshot);
            }
            finally
            {
                PersistenceGate.Release();
            }
        }

        private static bool IsTerminal(SimulationSessionStatus status) => status is
            SimulationSessionStatus.Completed or SimulationSessionStatus.Failed or
            SimulationSessionStatus.Interrupted or SimulationSessionStatus.Cancelled;
    }
}

public sealed class SessionRequestException(string message) : ArgumentException(message);

public sealed class SessionCapacityException(string message) : InvalidOperationException(message);
