using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Execution;
using Rowles.Morphogenesis.Experiments.Results;
using Rowles.Morphogenesis.Laboratory.Publication;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Tests.Laboratory;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit.Abstractions;

namespace Rowles.Morphogenesis.Tests.Laboratory.Determinism;

public sealed class SimulationSessionDeterminismTests
{
    private readonly ITestOutputHelper _output;

    public SimulationSessionDeterminismTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task UnsteeredSessionAndPublishedFramesMatchTheHeadlessTrajectory()
    {
        ExperimentManifest manifest = SessionTestFixture.CreateManifest(mcsCount: 5);
        ExperimentSimulationInstance headless = ExperimentSimulationFactory.Create(manifest, 0);
        for (int mcs = 0; mcs < manifest.McsCount; mcs++)
        {
            headless.Simulation.RunMcs();
        }

        int[] expectedFinal = new int[headless.Simulation.State.SiteCount];
        headless.Simulation.State.CopyCellIdsTo(expectedFinal);
        await using SimulationSession session = SimulationSessionFactory.Create(manifest, replicateIndex: 0);
        await using IAsyncEnumerator<SimulationFrameLease> frames = session.WatchFramesAsync().GetAsyncEnumerator();

        Assert.True(await frames.MoveNextAsync());
        SimulationFrameLease initial = frames.Current;
        Assert.Equal(0, initial.Mcs);
        Assert.Equal(1, initial.Sequence);
        int[] expectedInitial = new int[headless.Simulation.State.SiteCount];
        ExperimentSimulationInstance initialHeadless = ExperimentSimulationFactory.Create(manifest, 0);
        initialHeadless.Simulation.State.CopyCellIdsTo(expectedInitial);
        Assert.Equal(expectedInitial, initial.CellIds.ToArray());
        string initialHash = HashCellIds(initial.CellIds.ToArray());
        initial.Dispose();

        await session.StartAsync(Guid.NewGuid(), expectedRevision: 0);
        SimulationSessionSnapshot completed = await SessionTestFixture.WaitForStatusAsync(
            session,
            SimulationSessionStatus.Completed);

        Assert.True(await frames.MoveNextAsync());
        SimulationFrameLease final = frames.Current;
        Assert.Equal(manifest.McsCount, final.Mcs);
        Assert.True(final.Sequence > initial.Sequence);
        int[] sessionFinal = final.CellIds.ToArray();
        Assert.Equal(expectedFinal, sessionFinal);
        Assert.Equal(manifest.McsCount, completed.CurrentMcs);
        Assert.Equal(manifest.McsCount * (long)manifest.GridWidth * manifest.GridHeight, completed.Counters.Attempts);

        _output.WriteLine($"Initial MCS 0 cell-id hash: {initialHash}");
        _output.WriteLine($"Headless final cell-id hash: {HashCellIds(expectedFinal)}");
        _output.WriteLine($"Session final cell-id hash: {HashCellIds(sessionFinal)}");
    }

    [Fact]
    public async Task MeasurementsFollowManifestCadenceAndFinalMcsIndependentlyOfFrames()
    {
        ExperimentManifest manifest = SessionTestFixture.CreateManifest(mcsCount: 5) with
        {
            Measurements = SessionTestFixture.CreateManifest().Measurements with
            {
                EveryMcs = 2,
                IncludeMcsZero = true
            }
        };
        await using SimulationSession session = SimulationSessionFactory.Create(manifest, replicateIndex: 0);
        await using IAsyncEnumerator<MeasurementSample> measurements = session.WatchMeasurementsAsync().GetAsyncEnumerator();

        Assert.True(await measurements.MoveNextAsync());
        List<long> sampledMcs = [measurements.Current.Mcs];
        long revision = 0;
        for (int mcs = 1; mcs <= manifest.McsCount; mcs++)
        {
            SimulationCommandResult step = await session.StepAsync(Guid.NewGuid(), revision);
            revision = step.Revision;

            if (mcs % manifest.Measurements.EveryMcs == 0 || mcs == manifest.McsCount)
            {
                Assert.True(await measurements.MoveNextAsync());
                sampledMcs.Add(measurements.Current.Mcs);
            }
        }

        Assert.Equal(new long[] { 0, 2, 4, 5 }, sampledMcs);
        Assert.Equal(5, session.LatestMeasurement!.Mcs);
        Assert.Equal(SimulationSessionStatus.Completed, session.GetSnapshot().Status);
    }

    private static string HashCellIds(int[] cellIds) => Convert.ToHexString(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(cellIds)));
}
