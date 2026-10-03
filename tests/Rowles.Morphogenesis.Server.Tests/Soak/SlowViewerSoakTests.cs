using System.Diagnostics;
using System.Text.Json;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Laboratory.Publication;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Xunit;
using Xunit.Abstractions;

namespace Rowles.Morphogenesis.Server.Tests.Soak;

public sealed class SlowViewerSoakTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Soak")]
    public async Task OneSubscriberRemainsResponsiveDuringTenMinute512Run()
    {
        ExperimentManifest source = ExperimentManifest.ReadJson(
            Path.Combine(AppContext.BaseDirectory, "experiments", "canonical", "E02-control.json"));
        ExperimentManifest manifest = source with
        {
            GridWidth = 512,
            GridHeight = 512,
            BoundaryMode = BoundaryMode.Wall,
            McsCount = int.MaxValue,
            ReplicateCount = 1,
            Initialiser = source.Initialiser with { CellCount = 1_024 },
            Measurements = source.Measurements with
            {
                EveryMcs = 1_000,
                IncludeMcsZero = false,
                ValidateInvariantsEveryMcs = 0
            }
        };

        await using SimulationSession session = SimulationSessionFactory.Create(
            manifest,
            replicateIndex: 0,
            options: new SimulationSessionOptions { LivePublishMaxFps = 10 });
        _ = await session.StartAsync(Guid.NewGuid(), expectedRevision: 0);
        await using IAsyncEnumerator<SimulationFrameLease> frames = session.WatchFramesAsync().GetAsyncEnumerator();
        Assert.True(await frames.MoveNextAsync());
        frames.Current.Dispose();

        const int targetPublishFramesPerSecond = 10;
        const double minimumDeliveredFramesPerSecond = 9;
        TimeSpan soakDuration = TimeSpan.FromMinutes(10);
        Stopwatch timer = Stopwatch.StartNew();
        List<long> workingSetSamples = [];
        double nextMemorySampleSeconds = 30;
        int deliveredFrames = 0;
        long startingMcs = session.GetSnapshot().CurrentMcs;
        long previousMcs = startingMcs;
        while (timer.Elapsed < soakDuration)
        {
            Assert.True(await frames.MoveNextAsync());
            frames.Current.Dispose();
            deliveredFrames++;

            SimulationSessionSnapshot snapshot = session.GetSnapshot();
            Assert.Equal(SimulationSessionStatus.Running, snapshot.Status);
            Assert.True(snapshot.CurrentMcs > previousMcs, "The simulation stopped advancing while the viewer was connected.");
            previousMcs = snapshot.CurrentMcs;
            if (timer.Elapsed.TotalSeconds >= nextMemorySampleSeconds)
            {
                using Process process = Process.GetCurrentProcess();
                workingSetSamples.Add(process.WorkingSet64);
                nextMemorySampleSeconds += 30;
            }
        }

        SimulationSessionSnapshot final = session.GetSnapshot();
        double deliveredFramesPerSecond = deliveredFrames / timer.Elapsed.TotalSeconds;
        double mcsPerSecond = (final.CurrentMcs - startingMcs) / timer.Elapsed.TotalSeconds;
        double boundaryLimitedFramesPerSecond = mcsPerSecond /
            Math.Ceiling(mcsPerSecond / targetPublishFramesPerSecond);
        bool frameRateGateApplies = boundaryLimitedFramesPerSecond >= minimumDeliveredFramesPerSecond;
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Grid = "512x512",
            SubscriberCount = 1,
            DurationSeconds = timer.Elapsed.TotalSeconds,
            DeliveredFrames = deliveredFrames,
            DeliveredFramesPerSecond = deliveredFramesPerSecond,
            McsPerSecond = mcsPerSecond,
            BoundaryLimitedFramesPerSecond = boundaryLimitedFramesPerSecond,
            FrameRateGateApplies = frameRateGateApplies,
            CurrentMcs = final.CurrentMcs,
            WorkingSetSamples = workingSetSamples,
            Counters = final.Counters
        }));
        if (frameRateGateApplies)
        {
            Assert.True(
                deliveredFramesPerSecond >= minimumDeliveredFramesPerSecond,
                $"The 512² subscriber received {deliveredFramesPerSecond:F2} FPS, below {minimumDeliveredFramesPerSecond:F1}, while the measured MCS boundary rate could sustain {boundaryLimitedFramesPerSecond:F2} FPS.");
        }

        Assert.True(final.Counters.CapturedFrames > 1);
        Assert.True(final.Counters.PublishedFrames > 1);
        Assert.True(workingSetSamples.Count >= 18);
        long earliest = workingSetSamples.Take(5).Max();
        long latest = workingSetSamples.TakeLast(5).Max();
        Assert.True(
            latest <= earliest + 64L * 1024 * 1024,
            $"512² working set did not plateau: first-window peak {earliest:N0}, last-window peak {latest:N0}.");

        _ = await session.StopAsync(Guid.NewGuid(), session.GetSnapshot().Revision);
    }

    [Fact]
    [Trait("Category", "Soak")]
    public async Task FiveSecondSubscriberRemainsBoundedDuringThirtyMinuteRun()
    {
        ExperimentManifest source = ExperimentManifest.ReadJson(
            Path.Combine(AppContext.BaseDirectory, "experiments", "canonical", "E02-control.json"));
        ExperimentManifest manifest = source with
        {
            GridWidth = 256,
            GridHeight = 256,
            McsCount = int.MaxValue,
            ReplicateCount = 1,
            Measurements = source.Measurements with
            {
                EveryMcs = 1_000,
                IncludeMcsZero = false,
                ValidateInvariantsEveryMcs = 0
            }
        };

        await using SimulationSession session = SimulationSessionFactory.Create(
            manifest,
            replicateIndex: 0,
            options: new SimulationSessionOptions { LivePublishMaxFps = 20 });
        _ = await session.StartAsync(Guid.NewGuid(), expectedRevision: 0);
        await using IAsyncEnumerator<SimulationFrameLease> frames = session.WatchFramesAsync().GetAsyncEnumerator();
        Assert.True(await frames.MoveNextAsync());

        TimeSpan soakDuration = TimeSpan.FromMinutes(30);
        Stopwatch timer = Stopwatch.StartNew();
        List<long> workingSetSamples = [];
        double nextMemorySampleSeconds = 60;
        long previousMcs = session.GetSnapshot().CurrentMcs;
        while (timer.Elapsed < soakDuration)
        {
            SimulationFrameLease heldFrame = frames.Current;
            await Task.Delay(TimeSpan.FromSeconds(5));
            heldFrame.Dispose();
            Assert.True(await frames.MoveNextAsync());

            SimulationSessionSnapshot snapshot = session.GetSnapshot();
            Assert.Equal(SimulationSessionStatus.Running, snapshot.Status);
            Assert.True(snapshot.CurrentMcs > previousMcs, "The simulation stopped advancing while the viewer was slow.");
            previousMcs = snapshot.CurrentMcs;
            if (timer.Elapsed.TotalSeconds >= nextMemorySampleSeconds)
            {
                using Process process = Process.GetCurrentProcess();
                workingSetSamples.Add(process.WorkingSet64);
                nextMemorySampleSeconds += 60;
            }
        }

        SimulationSessionSnapshot final = session.GetSnapshot();
        Assert.True(final.Counters.CoalescedOrDroppedFrames > 0);
        Assert.True(final.Counters.CapturedFrames > 1);
        Assert.True(final.Counters.PublishedFrames > 1);
        Assert.True(workingSetSamples.Count >= 20);
        long earliest = workingSetSamples.Take(5).Max();
        long latest = workingSetSamples.TakeLast(5).Max();
        Assert.True(
            latest <= earliest + 64L * 1024 * 1024,
            $"Working set did not plateau: first-window peak {earliest:N0}, last-window peak {latest:N0}.");

        output.WriteLine(JsonSerializer.Serialize(new
        {
            DurationSeconds = timer.Elapsed.TotalSeconds,
            WorkingSetSamples = workingSetSamples,
            Counters = final.Counters
        }));
        _ = await session.StopAsync(Guid.NewGuid(), session.GetSnapshot().Revision);
    }
}
