using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Execution;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Tests.Laboratory;

namespace Rowles.Morphogenesis.Tests.Laboratory.Recording;

[Collection(RecordingTestCollection.Name)]
public sealed class SimulationRecorderTests
{
    [Fact]
    public async Task DefaultsKeepRecordingDisabledAndFixedOptionsAreValidated()
    {
        RecordingOptions options = new();
        Assert.False(options.Enabled);
        Assert.Equal(5, options.RecordEveryMcs);
        Assert.Equal(20, options.KeyframeEveryRecordedFrames);
        Assert.Equal(0.75, options.DeltaPromotionRatio);
        Assert.Equal(8, options.WriterQueueCapacity);

        ExperimentManifest manifest = SessionTestFixture.CreateManifest();
        Assert.Throws<ArgumentOutOfRangeException>(() => SimulationSessionFactory.Create(
            manifest, 0, recordingOptions: options with { RecordEveryMcs = 0 }));
        Assert.Throws<ArgumentException>(() => SimulationSessionFactory.Create(
            manifest, 0, recordingOptions: options with { KeyframeEveryRecordedFrames = 19 }));
        Assert.Throws<ArgumentException>(() => SimulationSessionFactory.Create(
            manifest, 0, recordingOptions: options with { DeltaPromotionRatio = 0.5 }));
        Assert.Throws<ArgumentException>(() => SimulationSessionFactory.Create(
            manifest, 0, recordingOptions: options with { WriterQueueCapacity = 7 }));
        Assert.Throws<ArgumentNullException>(() => SimulationSessionFactory.Create(
            manifest, 0, recordingOptions: options with { Enabled = true }));

        await using SimulationSession session = SimulationSessionFactory.Create(manifest, 0);
        Assert.Equal(RecordingState.Disabled, session.GetSnapshot().RecordingState);
        Assert.Null(session.GetSnapshot().RecordingFailure);
    }

    [Fact]
    public async Task RecordsMcsZeroCadenceAndTerminalKeyframe()
    {
        ExperimentManifest manifest = SessionTestFixture.CreateManifest(mcsCount: 12, width: 32, height: 32);
        InMemoryRecordingStore store = new();
        await using SimulationSession session = SimulationSessionFactory.Create(
            manifest,
            0,
            recordingOptions: new RecordingOptions { Enabled = true },
            recordingStore: store);

        Assert.True(
            session.GetSnapshot().RecordingState == RecordingState.Active,
            session.GetSnapshot().RecordingFailure);
        await StepToCompletionAsync(session);
        await WaitForRecordingStateAsync(session, RecordingState.Completed);

        EncodedRecordingFrame[] frames = store.GetStoredFrames();
        Assert.Equal([0, 5, 10, 12], frames.Select(frame => frame.Mcs));
        Assert.Equal(RecordingFrameKind.Keyframe, frames[0].Kind);
        Assert.Equal(RecordingFrameKind.Keyframe, frames[^1].Kind);
        Assert.Equal(12, store.GetCompletion()!.FinalMcs);
        Assert.Equal(4, store.GetCompletion()!.FrameCount);
        Assert.Equal(12, session.GetSnapshot().Revision);
    }

    [Fact]
    public async Task EveryTwentiethRecordedFrameIsAKeyframe()
    {
        ExperimentManifest manifest = SessionTestFixture.CreateManifest(mcsCount: 95, width: 32, height: 32);
        InMemoryRecordingStore store = new();
        await using SimulationSession session = SimulationSessionFactory.Create(
            manifest,
            0,
            recordingOptions: new RecordingOptions { Enabled = true },
            recordingStore: store);

        await StepToCompletionAsync(session);
        await WaitForRecordingStateAsync(session, RecordingState.Completed);

        EncodedRecordingFrame[] frames = store.GetStoredFrames();
        Assert.Equal(20, frames.Length);
        Assert.Equal(19, frames[19].Sequence);
        Assert.Equal(95, frames[19].Mcs);
        Assert.Equal(RecordingFrameKind.Keyframe, frames[19].Kind);
    }

    [Fact]
    public void DeltaPromotionUsesTheExactSeventyFivePercentBoundary()
    {
        Assert.False(SimulationRecorder.ShouldPromoteDelta(29, keyframePayloadSize: 40));
        Assert.True(SimulationRecorder.ShouldPromoteDelta(30, keyframePayloadSize: 40));
        Assert.True(SimulationRecorder.ShouldPromoteDelta(31, keyframePayloadSize: 40));
    }

    [Fact]
    public async Task ZeroChangeCandidateIsStoredAsAnEmptyDelta()
    {
        ExperimentManifest manifest = SessionTestFixture.CreateManifest(mcsCount: 5, width: 32, height: 32);
        await using SimulationSession metadataSession = SimulationSessionFactory.Create(manifest, 0);
        CoalescingLatticeChangeAccumulator accumulator = new(manifest.GridWidth * manifest.GridHeight);
        ExperimentSimulationInstance instance = ExperimentSimulationFactory.Create(manifest, 0, accumulator);
        InMemoryRecordingStore store = new();
        SimulationRecorder recorder = new(
            metadataSession.Metadata.SessionId,
            manifest,
            metadataSession.Metadata,
            instance.Simulation.State,
            accumulator,
            new RecordingOptions { Enabled = true },
            store,
            static (_, _) => { });

        recorder.RecordCompletedMcs(5);
        await recorder.CompleteAsync(finalMcs: 5);
        await recorder.DisposeAsync();

        EncodedRecordingFrame delta = Assert.Single(
            store.GetStoredFrames(),
            frame => frame.Kind == RecordingFrameKind.Delta);
        RecordedFrameEnvelope envelope = RecordingMessagePackCodec.Deserialize(delta.EnvelopeBytes);
        Assert.Equal([0], envelope.Payload);
    }

    [Fact]
    public async Task QueueFullFailsRecordingWithoutBlockingOrFailingSimulation()
    {
        TaskCompletionSource createEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        InMemoryRecordingStore store = new()
        {
            CreateBehavior = async cancellationToken =>
            {
                createEntered.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
        };
        const int MCSCount = 64;
        ExperimentManifest manifest = SessionTestFixture.CreateManifest(mcsCount: MCSCount, width: 16, height: 16);
        int outstandingBefore = ExactByteArrayPool.Instance.OutstandingCount;
        SimulationSession session = SimulationSessionFactory.Create(
            manifest,
            0,
            recordingOptions: new RecordingOptions { Enabled = true, RecordEveryMcs = 1 },
            recordingStore: store);
        try
        {
            await createEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await session.StartAsync(Guid.NewGuid(), expectedRevision: 0);
            await WaitForRecordingStateAsync(session, RecordingState.Failed);

            SimulationSessionSnapshot completed = await WaitForSessionStatusAsync(session, SimulationSessionStatus.Completed);
            Assert.Equal(MCSCount, completed.CurrentMcs);
            Assert.Equal(RecordingState.Failed, completed.RecordingState);
            Assert.Contains(nameof(RecordingBackpressureException), completed.RecordingFailure);
        }
        finally
        {
            await session.DisposeAsync();
        }

        Assert.Equal(outstandingBefore, ExactByteArrayPool.Instance.OutstandingCount);
    }

    [Fact]
    public async Task StoreFailureDoesNotChangeTheScientificTrajectory()
    {
        ExperimentManifest manifest = SessionTestFixture.CreateManifest(mcsCount: 6, width: 32, height: 32);
        InMemoryRecordingStore store = new()
        {
            AppendBehavior = static (_, _) => ValueTask.FromException(new IOException("test store failure"))
        };
        SimulationSession recorded = SimulationSessionFactory.Create(
            manifest,
            0,
            recordingOptions: new RecordingOptions { Enabled = true, RecordEveryMcs = 1 },
            recordingStore: store);
        await WaitForRecordingStateAsync(recorded, RecordingState.Failed);
        Assert.Contains("test store failure", recorded.GetSnapshot().RecordingFailure);
        await StepToCompletionAsync(recorded);
        SimulationSessionSnapshot recordedFinal = recorded.GetSnapshot();
        int[] recordedLattice = await CopyLatestFrameAsync(recorded);
        await recorded.DisposeAsync();

        await using SimulationSession unrecorded = SimulationSessionFactory.Create(manifest, 0);
        await StepToCompletionAsync(unrecorded);
        int[] unrecordedLattice = await CopyLatestFrameAsync(unrecorded);
        SimulationSessionSnapshot unrecordedFinal = unrecorded.GetSnapshot();

        Assert.Equal(unrecordedLattice, recordedLattice);
        Assert.Equal(unrecordedFinal.Counters.Attempts, recordedFinal.Counters.Attempts);
        Assert.Equal(unrecordedFinal.Counters.Accepted, recordedFinal.Counters.Accepted);
        Assert.Equal(unrecordedFinal.Counters.Rejected, recordedFinal.Counters.Rejected);
        Assert.Equal(unrecordedFinal.Counters.NoOps, recordedFinal.Counters.NoOps);
        Assert.Equal(unrecordedFinal.Counters.ConnectivityFallbacks, recordedFinal.Counters.ConnectivityFallbacks);
        Assert.Equal(SimulationSessionStatus.Completed, recordedFinal.Status);
        Assert.Equal(RecordingState.Failed, recordedFinal.RecordingState);
    }

    [Fact]
    public async Task SuccessfulRecordingDoesNotChangeTheScientificTrajectoryOrCounters()
    {
        ExperimentManifest manifest = SessionTestFixture.CreateManifest(mcsCount: 8, width: 32, height: 32);
        InMemoryRecordingStore store = new();
        SimulationSession recorded = SimulationSessionFactory.Create(
            manifest,
            0,
            recordingOptions: new RecordingOptions { Enabled = true },
            recordingStore: store);
        await StepToCompletionAsync(recorded);
        await WaitForRecordingStateAsync(recorded, RecordingState.Completed);
        int[] recordedLattice = await CopyLatestFrameAsync(recorded);
        SimulationSessionSnapshot recordedFinal = recorded.GetSnapshot();
        Assert.Equal(RecordingState.Completed, recordedFinal.RecordingState);
        await recorded.DisposeAsync();

        await using SimulationSession unrecorded = SimulationSessionFactory.Create(manifest, 0);
        await StepToCompletionAsync(unrecorded);
        int[] unrecordedLattice = await CopyLatestFrameAsync(unrecorded);
        SimulationSessionSnapshot unrecordedFinal = unrecorded.GetSnapshot();

        Assert.Equal(unrecordedLattice, recordedLattice);
        Assert.Equal(unrecordedFinal.Counters.Attempts, recordedFinal.Counters.Attempts);
        Assert.Equal(unrecordedFinal.Counters.Accepted, recordedFinal.Counters.Accepted);
        Assert.Equal(unrecordedFinal.Counters.Rejected, recordedFinal.Counters.Rejected);
        Assert.Equal(unrecordedFinal.Counters.NoOps, recordedFinal.Counters.NoOps);
        Assert.Equal(unrecordedFinal.Counters.ConnectivityFallbacks, recordedFinal.Counters.ConnectivityFallbacks);
        Assert.Equal(SimulationSessionStatus.Completed, recordedFinal.Status);

    }

    [Fact]
    public async Task PooledEnvelopeBuffersReturnAfterSuccessfulAndFailedDisposal()
    {
        int outstandingBefore = ExactByteArrayPool.Instance.OutstandingCount;
        ExperimentManifest successManifest = SessionTestFixture.CreateManifest(mcsCount: 2, width: 16, height: 16);
        InMemoryRecordingStore successStore = new();
        SimulationSession successful = SimulationSessionFactory.Create(
            successManifest,
            0,
            recordingOptions: new RecordingOptions { Enabled = true, RecordEveryMcs = 1 },
            recordingStore: successStore);
        await StepToCompletionAsync(successful);
        await WaitForRecordingStateAsync(successful, RecordingState.Completed);
        await successful.DisposeAsync();
        Assert.Equal(outstandingBefore, ExactByteArrayPool.Instance.OutstandingCount);

        TaskCompletionSource createEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        InMemoryRecordingStore failedStore = new()
        {
            CreateBehavior = async cancellationToken =>
            {
                createEntered.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
        };
        ExperimentManifest failureManifest = SessionTestFixture.CreateManifest(mcsCount: 1_000_000, width: 16, height: 16);
        SimulationSession failed = SimulationSessionFactory.Create(
            failureManifest,
            0,
            recordingOptions: new RecordingOptions { Enabled = true, RecordEveryMcs = 1 },
            recordingStore: failedStore);
        await createEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await failed.StartAsync(Guid.NewGuid(), expectedRevision: 0);
        await WaitForRecordingStateAsync(failed, RecordingState.Failed);
        await failed.StopAsync(Guid.NewGuid(), expectedRevision: 1);
        await failed.DisposeAsync();
        Assert.Equal(outstandingBefore, ExactByteArrayPool.Instance.OutstandingCount);
    }

    private static async Task StepToCompletionAsync(SimulationSession session)
    {
        while (session.GetSnapshot().Status is SimulationSessionStatus.Created or SimulationSessionStatus.Paused)
        {
            SimulationSessionSnapshot snapshot = session.GetSnapshot();
            SimulationCommandResult result = await session.StepAsync(Guid.NewGuid(), snapshot.Revision);
            Assert.Equal(SimulationCommandDisposition.Applied, result.Disposition);
        }

        Assert.Equal(SimulationSessionStatus.Completed, session.GetSnapshot().Status);
    }

    private static async Task WaitForRecordingStateAsync(SimulationSession session, RecordingState state)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<SimulationSessionSnapshot> snapshots =
            session.WatchStateAsync(timeout.Token).GetAsyncEnumerator();
        while (await snapshots.MoveNextAsync().ConfigureAwait(false))
        {
            if (snapshots.Current.RecordingState == state)
            {
                return;
            }
        }

        throw new TimeoutException($"The recording did not reach {state}.");
    }

    private static async Task<SimulationSessionSnapshot> WaitForSessionStatusAsync(
        SimulationSession session,
        SimulationSessionStatus status)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<SimulationSessionSnapshot> snapshots =
            session.WatchStateAsync(timeout.Token).GetAsyncEnumerator();
        while (await snapshots.MoveNextAsync().ConfigureAwait(false))
        {
            if (snapshots.Current.Status == status)
                return snapshots.Current;
        }

        throw new TimeoutException($"The session did not reach {status}.");
    }

    private static async Task<int[]> CopyLatestFrameAsync(SimulationSession session)
    {
        await using IAsyncEnumerator<Rowles.Morphogenesis.Laboratory.Publication.SimulationFrameLease> frames =
            session.WatchFramesAsync().GetAsyncEnumerator();
        Assert.True(await frames.MoveNextAsync().ConfigureAwait(false));
        return frames.Current.CellIds.ToArray();
    }
}
