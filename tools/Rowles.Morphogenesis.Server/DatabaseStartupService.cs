using Rowles.Morphogenesis.Server.Persistence;

namespace Rowles.Morphogenesis.Server;

public sealed class DatabaseStartupService : IHostedService
{
    private readonly LaboratoryDatabase _database;
    private readonly ILogger<DatabaseStartupService> _logger;

    public DatabaseStartupService(LaboratoryDatabase database, ILogger<DatabaseStartupService> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        int recoveredRuns = await _database.RecoverOrphanedSessionsAsync(DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        if (recoveredRuns > 0)
        {
            _logger.LogWarning(
                "Normalised {RecoveredRunCount} persisted session or recording states whose in-memory owner was lost during startup; live sessions were marked Interrupted and active recordings were marked Failed",
                recoveredRuns);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
