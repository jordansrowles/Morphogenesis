using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Laboratory.Playback;
using Rowles.Morphogenesis.Laboratory.Publication;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Tests.Laboratory;
using Rowles.Morphogenesis.Tests.Laboratory.Recording;

namespace Rowles.Morphogenesis.Tests.Laboratory.Playback;

[Collection(RecordingTestCollection.Name)]
public sealed class SimulationSourceTests
{
    [Fact]
    public async Task RecordedSourceReconstructsKeyframesAndDeltaChainsForSeekAndStepping()
    {
        (ExperimentManifest manifest, SimulationMetadata metadata) = await CreateMetadataAsync();
        Guid sessionId = metadata.SessionId;
        int[] atMcs0 = CreateLattice(256, 1);
        int[] atMcs5 = atMcs0.ToArray();
        atMcs5[1] = 2;
        atMcs5[7] = 1;
        int[] atMcs10 = atMcs5.ToArray();
        atMcs10[0] = 1;
        atMcs10[7] = 2;
        int[] atMcs15 = atMcs10.ToArray();
        atMcs15[50] = 3;
        int[] atMcs20 = atMcs15.ToArray();
        atMcs20[10] = 2;

        InMemoryRecordingStore store = new();
        store.Seed(
            RecordingTestFixture.CreateHeader(sessionId, manifest, metadata),
            RecordingTestFixture.EncodeFrame(16, 16, 0, 0, RecordingFrameKind.Keyframe, atMcs0),
            RecordingTestFixture.EncodeFrame(16, 16, 1, 5, RecordingFrameKind.Delta, atMcs5, [1, 7], [2, 1]),
            RecordingTestFixture.EncodeFrame(16, 16, 2, 10, RecordingFrameKind.Delta, atMcs10, [0, 7], [1, 2]),
            RecordingTestFixture.EncodeFrame(16, 16, 3, 15, RecordingFrameKind.Keyframe, atMcs15),
            RecordingTestFixture.EncodeFrame(16, 16, 4, 20, RecordingFrameKind.Delta, atMcs20, [10], [2]));

        await using RecordedSimulationSource source = await RecordedSimulationSource.OpenAsync(store, sessionId);
        Assert.Equal(new SimulationSourceCapabilities(false, true, true, false, true), source.Capabilities);
        Assert.Equal(0, source.CurrentMcs);
        Assert.Equal(atMcs0, await CopyCurrentFrameAsync(source));

        await source.SeekAsync(10);
        Assert.Equal(10, source.CurrentMcs);
        Assert.Equal(atMcs10, await CopyCurrentFrameAsync(source));

        await source.SeekAsync(14);
        Assert.Equal(10, source.CurrentMcs);
        await source.StepForwardAsync();
        Assert.Equal(15, source.CurrentMcs);
        Assert.Equal(atMcs15, await CopyCurrentFrameAsync(source));

        await source.StepBackwardAsync();
        Assert.Equal(10, source.CurrentMcs);
        Assert.Equal(atMcs10, await CopyCurrentFrameAsync(source));
        await source.StepForwardAsync();
        await source.StepForwardAsync();
        Assert.Equal(20, source.CurrentMcs);
        Assert.Equal(atMcs20, await CopyCurrentFrameAsync(source));
    }

    [Fact]
    public async Task OneShotReconstructorReturnsTheLatestFrameAndBoundsWorkspaceConcurrency()
    {
        (ExperimentManifest manifest, SimulationMetadata metadata) = await CreateMetadataAsync();
        int[] atMcs0 = CreateLattice(256, 1);
        int[] atMcs5 = atMcs0.ToArray();
        atMcs5[1] = 2;
        int[] atMcs10 = atMcs5.ToArray();
        atMcs10[7] = 2;
        InMemoryRecordingStore store = new();
        store.Seed(
            RecordingTestFixture.CreateHeader(metadata.SessionId, manifest, metadata),
            RecordingTestFixture.EncodeFrame(16, 16, 0, 0, RecordingFrameKind.Keyframe, atMcs0),
            RecordingTestFixture.EncodeFrame(16, 16, 1, 5, RecordingFrameKind.Delta, atMcs5, [1], [2]),
            RecordingTestFixture.EncodeFrame(16, 16, 2, 10, RecordingFrameKind.Delta, atMcs10, [7], [2]));

        RecordingFrameReconstructor reconstructor = new(maximumConcurrentReconstructions: 1, maximumSiteCount: 256);
        ReconstructedRecordingFrame first = await reconstructor.ReconstructAsync(store, metadata.SessionId, mcs: 14);
        Assert.Equal(2, first.Sequence);
        Assert.Equal(10, first.Mcs);
        Assert.Equal(atMcs10, first.CellIds.ToArray());

        Task<ReconstructedRecordingFrame> secondTask = reconstructor.ReconstructAsync(store, metadata.SessionId, mcs: 10).AsTask();
        Assert.False(secondTask.IsCompleted);
        first.Dispose();

        using ReconstructedRecordingFrame second = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(atMcs10, second.CellIds.ToArray());
    }

    [Fact]
    public async Task RecordedPlaybackRateValidationAndPlayPauseWork()
    {
        (ExperimentManifest manifest, SimulationMetadata metadata) = await CreateMetadataAsync();
        int[] atMcs0 = CreateLattice(256, 1);
        int[] atMcs5 = atMcs0.ToArray();
        atMcs5[10] = 2;
        int[] atMcs10 = atMcs5.ToArray();
        atMcs10[11] = 2;
        InMemoryRecordingStore store = new();
        store.Seed(
            RecordingTestFixture.CreateHeader(metadata.SessionId, manifest, metadata),
            RecordingTestFixture.EncodeFrame(16, 16, 0, 0, RecordingFrameKind.Keyframe, atMcs0),
            RecordingTestFixture.EncodeFrame(16, 16, 1, 5, RecordingFrameKind.Delta, atMcs5, [10], [2]),
            RecordingTestFixture.EncodeFrame(16, 16, 2, 10, RecordingFrameKind.Delta, atMcs10, [11], [2]));

        await using RecordedSimulationSource source = await RecordedSimulationSource.OpenAsync(store, metadata.SessionId);
        foreach (double rate in new[] { 0.25, 0.5, 1, 2, 4 })
        {
            source.SetPlaybackRate(rate);
            Assert.Equal(rate, source.PlaybackRate);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => source.SetPlaybackRate(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.SetPlaybackRate(1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.SetPlaybackRate(double.NaN));

        source.SetPlaybackRate(4);
        await source.PlayAsync();
        await WaitForMcsAsync(source, targetMcs: 5);
        await source.PauseAsync();
        long pausedMcs = source.CurrentMcs;
        await Task.Delay(80);
        Assert.Equal(pausedMcs, source.CurrentMcs);

        await using IAsyncEnumerator<SimulationFrameLease> frames = source.WatchFramesAsync().GetAsyncEnumerator();
        Assert.True(await frames.MoveNextAsync());
        Assert.Equal(pausedMcs, frames.Current.Mcs);
        frames.Current.Dispose();
    }

    [Fact]
    public async Task LiveSourceCapabilitiesAndControlsMapToTheSession()
    {
        ExperimentManifest manifest = SessionTestFixture.CreateManifest(mcsCount: 100_000);
        await using SimulationSession session = SimulationSessionFactory.Create(manifest, 0);
        await using LiveSimulationSource source = new(session);

        Assert.Equal(new SimulationSourceCapabilities(true, false, false, true, false), source.Capabilities);
        using SimulationFrameLease initial = await source.GetCurrentFrameAsync();
        Assert.Equal(0, initial.Mcs);
        Assert.Equal(session.Metadata.SessionId, initial.SessionId);
        Assert.Equal(256, initial.CellIds.Length);

        await Assert.ThrowsAsync<NotSupportedException>(async () => await source.SeekAsync(5));
        await Assert.ThrowsAsync<NotSupportedException>(async () => await source.StepBackwardAsync());

        await source.PlayAsync();
        await source.PauseAsync();
        Assert.Equal(SimulationSessionStatus.Paused, session.GetSnapshot().Status);
        long pausedMcs = source.CurrentMcs;
        await source.StepForwardAsync();
        Assert.Equal(pausedMcs + 1, source.CurrentMcs);
        Assert.Equal(SimulationSessionStatus.Paused, session.GetSnapshot().Status);
    }

    [Fact]
    public async Task RecordedSourceRejectsUnsupportedHeaderVersions()
    {
        (ExperimentManifest manifest, SimulationMetadata metadata) = await CreateMetadataAsync();
        int[] lattice = CreateLattice(256, 1);
        RecordingHeader header = RecordingTestFixture.CreateHeader(metadata.SessionId, manifest, metadata) with
        {
            DeltaCodecVersion = RecordingFormat.DeltaCodecVersion + 1
        };
        InMemoryRecordingStore store = new();
        store.Seed(
            header,
            RecordingTestFixture.EncodeFrame(16, 16, 0, 0, RecordingFrameKind.Keyframe, lattice));

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await RecordedSimulationSource.OpenAsync(store, metadata.SessionId));
    }

    private static async Task<(ExperimentManifest Manifest, SimulationMetadata Metadata)> CreateMetadataAsync()
    {
        ExperimentManifest manifest = SessionTestFixture.CreateManifest(mcsCount: 20, width: 16, height: 16);
        await using SimulationSession session = SimulationSessionFactory.Create(manifest, 0);
        return (manifest, session.Metadata);
    }

    private static int[] CreateLattice(int siteCount, int value)
    {
        int[] lattice = new int[siteCount];
        Array.Fill(lattice, value);
        lattice[0] = 0;
        return lattice;
    }

    private static async Task<int[]> CopyCurrentFrameAsync(RecordedSimulationSource source)
    {
        using SimulationFrameLease frame = await source.GetCurrentFrameAsync();
        return frame.CellIds.ToArray();
    }

    private static async Task WaitForMcsAsync(RecordedSimulationSource source, long targetMcs)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (source.CurrentMcs < targetMcs)
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
