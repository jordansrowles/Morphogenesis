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
        int interruptedRuns = await _database.MarkRunningInterruptedAsync(DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        if (interruptedRuns > 0)
        {
            _logger.LogWarning(
                "Marked {InterruptedRunCount} persisted running sessions as Interrupted during startup recovery",
                interruptedRuns);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
