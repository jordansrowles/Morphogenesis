using Rowles.Morphogenesis.Server.Experiments;

namespace Rowles.Morphogenesis.Server;

public sealed class ExperimentCatalogStartupService : IHostedService
{
    public ExperimentCatalogStartupService(ExperimentCatalog catalog)
    {
        _ = catalog;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
