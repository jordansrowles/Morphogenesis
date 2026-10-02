using Rowles.Morphogenesis.Server.Persistence;

namespace Rowles.Morphogenesis.Server;

public sealed class DatabaseStartupService : IHostedService
{
    private readonly LaboratoryDatabase _database;

    public DatabaseStartupService(LaboratoryDatabase database)
    {
        _database = database;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _database.MarkRunningInterruptedAsync(DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
