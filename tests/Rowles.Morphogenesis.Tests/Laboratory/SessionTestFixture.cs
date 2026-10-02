using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Tests.Experiments;

namespace Rowles.Morphogenesis.Tests.Laboratory;

internal static class SessionTestFixture
{
    internal static ExperimentManifest CreateManifest(
        int mcsCount = 4,
        int width = 16,
        int height = 16) => ExperimentManifestFactory.Create() with
        {
            GridWidth = width,
            GridHeight = height,
            McsCount = mcsCount,
            ReplicateCount = 1
        };

    internal static async Task<SimulationSessionSnapshot> WaitForStatusAsync(
        SimulationSession session,
        SimulationSessionStatus status)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<SimulationSessionSnapshot> updates =
            session.WatchStateAsync(timeout.Token).GetAsyncEnumerator();

        while (await updates.MoveNextAsync().ConfigureAwait(false))
        {
            if (updates.Current.Status == status)
            {
                return updates.Current;
            }
        }

        throw new TimeoutException($"The session did not reach {status} before its state stream ended.");
    }
}
