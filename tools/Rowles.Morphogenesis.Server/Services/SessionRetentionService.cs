using Rowles.Morphogenesis.Server.Configuration;
using Rowles.Morphogenesis.Server.Persistence;
using Rowles.Morphogenesis.Server.Sessions;

namespace Rowles.Morphogenesis.Server.Services;

public sealed class SessionRetentionService : BackgroundService
{
    private readonly LaboratoryResourceLimits _limits;
    private readonly SimulationSessionRegistry _registry;
    private readonly LaboratoryDatabase _database;
    private readonly ILogger<SessionRetentionService> _logger;

    public SessionRetentionService(
        LaboratoryResourceLimits limits,
        SimulationSessionRegistry registry,
        LaboratoryDatabase database,
        ILogger<SessionRetentionService> logger)
    {
        _limits = limits;
        _registry = registry;
        _database = database;
        _logger = logger;
    }

    public async Task<RetentionSweepResult> SweepAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        int idleSessionsCancelled = await _registry.CancelIdleSessionsAsync(nowUtc, cancellationToken).ConfigureAwait(false);
        RetentionDeletionResult deletion = await _database.DeleteExpiredRunsAndEnforceCapsAsync(
            nowUtc - _limits.TerminalRetention,
            _limits.MaxPersistedRuns,
            _limits.MaxStoredRecordingBytes,
            cancellationToken).ConfigureAwait(false);
        foreach (Guid sessionId in deletion.DeletedRunIds)
            await _registry.RemovePersistedSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (idleSessionsCancelled > 0 || deletion.DeletedRunIds.Count > 0)
        {
            _logger.LogInformation(
                "Retention sweep cancelled {IdleSessionCount} idle sessions and deleted {RunCount} persisted runs; {RemainingRunCount} runs and {StoredRecordingBytes} recording bytes remain",
                idleSessionsCancelled,
                deletion.DeletedRunIds.Count,
                deletion.RemainingRunCount,
                deletion.RemainingRecordingBytes);
        }

        return new RetentionSweepResult(
            idleSessionsCancelled,
            deletion.DeletedRunIds.Count,
            deletion.RemainingRunCount,
            deletion.RemainingRecordingBytes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(_limits.RetentionSweepInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                await SweepAsync(DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}

public sealed record RetentionSweepResult(
    int IdleSessionsCancelled,
    int PersistedRunsDeleted,
    long RemainingRunCount,
    long RemainingRecordingBytes);
