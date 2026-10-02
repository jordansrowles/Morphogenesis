using System.Collections.Concurrent;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Results;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Server.Configuration;
using Rowles.Morphogenesis.Server.Experiments;
using Rowles.Morphogenesis.Server.Persistence;

namespace Rowles.Morphogenesis.Server.Sessions;

public sealed class SimulationSessionRegistry : IHostedService, IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, ActiveSession> _sessions = new();
    private readonly ExperimentCatalog _catalog;
    private readonly LaboratoryDatabase _database;
    private readonly SqliteRecordingStore _recordingStore;
    private readonly CancellationTokenSource _stopping = new();
    private int _disposed;

    public SimulationSessionRegistry(
        ExperimentCatalog catalog,
        LaboratoryDatabase database,
        SqliteRecordingStore recordingStore)
    {
        _catalog = catalog;
        _database = database;
        _recordingStore = recordingStore;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await DisposeSessionsAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
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
        if (entry.GridWidth > 512 || entry.GridHeight > 512 || (long)entry.GridWidth * entry.GridHeight > 262_144)
            throw new SessionRequestException("The experiment lattice exceeds the interactive session limit.");
        if (request.LivePublishMaxFps is < 1 or > 20)
            throw new SessionRequestException("Live publish FPS must be between 1 and 20.");

        ExperimentManifest manifest = _catalog.GetManifest(entry);
        RecordingOptions recordingOptions = new() { Enabled = request.RecordingEnabled };
        SimulationSession session = SimulationSessionFactory.Create(
            manifest,
            request.ReplicateIndex,
            new SimulationSessionOptions { LivePublishMaxFps = request.LivePublishMaxFps },
            recordingOptions,
            request.RecordingEnabled ? _recordingStore : null);
        ActiveSession active = new(session, manifest, DateTimeOffset.UtcNow);
        try
        {
            if (request.RecordingEnabled)
            {
                await _recordingStore.WaitForRunCreationAsync(session.Metadata.SessionId, cancellationToken).ConfigureAwait(false);
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

            active.StartObservers(_stopping.Token, _database);
            await PersistSnapshotAsync(active, session.GetSnapshot().LatestMeasurement, cancellationToken).ConfigureAwait(false);
            return await GetDtoAsync(active, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (_sessions.TryRemove(session.Metadata.SessionId, out _))
                await active.DisposeAsync().ConfigureAwait(false);
            else
                await session.DisposeAsync().ConfigureAwait(false);
            throw;
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

    private async Task DisposeSessionsAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _stopping.Cancel();
        ActiveSession[] sessions = _sessions.Values.ToArray();
        await Task.WhenAll(sessions.Select(active => active.StopObserversAsync())).ConfigureAwait(false);
        foreach (ActiveSession active in sessions)
        {
            await active.DisposeAsync().ConfigureAwait(false);
        }
        _sessions.Clear();
        _stopping.Dispose();
    }

    public ValueTask DisposeAsync() => new(DisposeSessionsAsync());

    private sealed class ActiveSession : IAsyncDisposable
    {
        private readonly CancellationTokenSource _observerCancellation = new();
        private readonly object _timeGate = new();
        private long _lastProgressMcs;
        private long _lastPersistedRevision = -1;
        private SimulationSessionStatus _lastPersistedStatus;
        private Task[] _observers = [];

        internal ActiveSession(SimulationSession session, ExperimentManifest manifest, DateTimeOffset createdAtUtc)
        {
            Session = session;
            Manifest = manifest;
            CreatedAtUtc = createdAtUtc;
        }

        internal SimulationSession Session { get; }
        internal ExperimentManifest Manifest { get; }
        internal DateTimeOffset CreatedAtUtc { get; }
        internal SemaphoreSlim PersistenceGate { get; } = new(1, 1);
        internal DateTimeOffset? StartedAtUtc { get; private set; }
        internal DateTimeOffset? CompletedAtUtc { get; private set; }

        internal void StartObservers(CancellationToken registryStopping, LaboratoryDatabase database)
        {
            CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(registryStopping, _observerCancellation.Token);
            _observers =
            [
                ObserveStateAsync(linked.Token, database),
                ObserveMeasurementsAsync(linked.Token, database),
                PersistProgressAsync(linked.Token, database)
            ];
            _ = Task.WhenAll(_observers).ContinueWith(
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
            lock (_timeGate)
            {
                if (snapshot.Status == SimulationSessionStatus.Running)
                    StartedAtUtc ??= DateTimeOffset.UtcNow;
                if (IsTerminal(snapshot.Status))
                    CompletedAtUtc ??= DateTimeOffset.UtcNow;
            }
        }

        internal async Task StopObserversAsync()
        {
            _observerCancellation.Cancel();
            try
            {
                await Task.WhenAll(_observers).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopObserversAsync().ConfigureAwait(false);
            await Session.DisposeAsync().ConfigureAwait(false);
            _observerCancellation.Dispose();
            PersistenceGate.Dispose();
        }

        private async Task ObserveStateAsync(CancellationToken cancellationToken, LaboratoryDatabase database)
        {
            await foreach (SimulationSessionSnapshot snapshot in Session.WatchStateAsync(cancellationToken).ConfigureAwait(false))
            {
                if (snapshot.Revision != _lastPersistedRevision || snapshot.Status != _lastPersistedStatus)
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
