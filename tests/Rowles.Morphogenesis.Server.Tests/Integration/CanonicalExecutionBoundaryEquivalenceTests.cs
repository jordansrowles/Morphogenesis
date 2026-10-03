using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Execution;
using Rowles.Morphogenesis.Experiments.Results;
using Rowles.Morphogenesis.Laboratory.Publication;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Snapshots;
using Rowles.Morphogenesis.Server.Sessions;
using Rowles.Morphogenesis.Server.Tests.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Rowles.Morphogenesis.Server.Tests.Integration;

public sealed class CanonicalExecutionBoundaryEquivalenceTests(ITestOutputHelper output)
{
    private static readonly string[] _canonicalExperiments =
    [
        "E00-single-cell-relaxation",
        "E02-control",
        "E02-sorting"
    ];

    [Fact]
    public async Task CanonicalFinalStatesAndCountersMatchAcrossHeadlessSessionAndWebApiRuns()
    {
        using TemporaryLaboratoryRoot root = new();
        using LaboratoryFactory factory = new(root.Path, longRunning: false);
        using HttpClient http = factory.CreateClient();
        SimulationSessionRegistry registry = factory.Services.GetRequiredService<SimulationSessionRegistry>();

        foreach (string experimentId in _canonicalExperiments)
        {
            string manifestPath = Path.Combine(AppContext.BaseDirectory, "experiments", "canonical", $"{experimentId}.json");
            ExperimentManifest manifest = ExperimentManifest.ReadJson(manifestPath);
            ExperimentManifest headlessManifest = manifest with
            {
                Measurements = manifest.Measurements with { SnapshotEveryMcs = manifest.McsCount }
            };
            ExperimentReplicateResult headless = ExperimentRunner.RunReplicate(headlessManifest, replicateIndex: 0);
            Assert.Equal(ReplicateRunStatus.Succeeded, headless.Status);
            ExperimentSnapshot expectedSnapshot = Assert.Single(headless.Snapshots, snapshot => snapshot.Mcs == manifest.McsCount);
            string expectedHash = HashCellIds(expectedSnapshot.CellIds);
            output.WriteLine($"{experimentId} headless: {Format(headless)} sha256={expectedHash}");

            ClosureResult withoutSubscriber = await RunLocalSessionAsync(manifest, withSubscriber: false, withRecording: false);
            AssertMatches(headless, expectedSnapshot.CellIds, withoutSubscriber);
            output.WriteLine($"{experimentId} session/no-subscriber: {Format(withoutSubscriber)} sha256={HashCellIds(withoutSubscriber.CellIds)}");

            ClosureResult withSubscriber = await RunLocalSessionAsync(manifest, withSubscriber: true, withRecording: false);
            AssertMatches(headless, expectedSnapshot.CellIds, withSubscriber);
            output.WriteLine($"{experimentId} session/subscriber: {Format(withSubscriber)} sha256={HashCellIds(withSubscriber.CellIds)}");

            ClosureResult withRecording = await RunLocalSessionAsync(manifest, withSubscriber: false, withRecording: true);
            AssertMatches(headless, expectedSnapshot.CellIds, withRecording);
            Assert.True(withRecording.RecordingFrameCount > 0);
            output.WriteLine($"{experimentId} session/recording: {Format(withRecording)} sha256={HashCellIds(withRecording.CellIds)}");

            ClosureResult webRun = await RunWebApiSessionAsync(http, registry, manifest);
            AssertMatches(headless, expectedSnapshot.CellIds, webRun);
            output.WriteLine($"{experimentId} web-api/no-steering: {Format(webRun)} sha256={HashCellIds(webRun.CellIds)}");
        }
    }

    private static async Task<ClosureResult> RunLocalSessionAsync(
        ExperimentManifest manifest,
        bool withSubscriber,
        bool withRecording)
    {
        ProbeRecordingStore recordingStore = new();
        RecordingOptions resolvedRecordingOptions = new()
        {
            Enabled = withRecording,
            RecordEveryMcs = 5
        };
        await using SimulationSession session = SimulationSessionFactory.Create(
            manifest,
            replicateIndex: 0,
            options: new SimulationSessionOptions { LivePublishMaxFps = 20 },
            recordingOptions: resolvedRecordingOptions,
            recordingStore: withRecording ? recordingStore : null);

        using CancellationTokenSource subscriberCancellation = new(TimeSpan.FromSeconds(30));
        IAsyncEnumerator<SimulationFrameLease>? subscriber = null;
        if (withSubscriber)
        {
            subscriber = session.WatchFramesAsync(subscriberCancellation.Token).GetAsyncEnumerator();
            Assert.True(await subscriber.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, subscriber.Current.Mcs);
            subscriber.Current.Dispose();
        }

        try
        {
            await StartAsync(session);
            SimulationSessionSnapshot snapshot = await WaitForCompletionAsync(session);
            if (withRecording)
            {
                snapshot = await WaitForRecordingCompletionAsync(session);
                Assert.True(recordingStore.FrameCount > 0);
                Assert.NotNull(recordingStore.Completion);
            }

            int[] cellIds = await ReadLatestFrameAsync(session);
            return new ClosureResult(
                cellIds,
                snapshot,
                session.Metadata.RunIdentity,
                withRecording ? recordingStore.FrameCount : 0);
        }
        finally
        {
            subscriberCancellation.Cancel();
            if (subscriber is not null)
            {
                if (subscriber.Current is not null)
                    subscriber.Current.Dispose();
                await subscriber.DisposeAsync();
            }
        }
    }

    private static async Task<ClosureResult> RunWebApiSessionAsync(
        HttpClient http,
        SimulationSessionRegistry registry,
        ExperimentManifest manifest)
    {
        using HttpResponseMessage createResponse = await http.PostAsJsonAsync(
            "/api/sessions",
            new CreateSessionRequest(manifest.ExperimentId, ReplicateIndex: 0, RecordingEnabled: false, LivePublishMaxFps: 20));
        createResponse.EnsureSuccessStatusCode();
        SessionDto created = (await createResponse.Content.ReadFromJsonAsync<SessionDto>())!;

        Assert.True(registry.TryGetSession(created.SessionId, out SimulationSession session));
        using CancellationTokenSource frameTimeout = new(TimeSpan.FromSeconds(30));
        await using IAsyncEnumerator<SimulationFrameLease> frames =
            session.WatchFramesAsync(frameTimeout.Token).GetAsyncEnumerator();
        Assert.True(await frames.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, frames.Current.Mcs);
        frames.Current.Dispose();
        Task<int[]> finalFrameTask = ReadFrameAtMcsAsync(frames, manifest.McsCount, frameTimeout.Token);

        using HttpResponseMessage startResponse = await http.PostAsJsonAsync(
            $"/api/sessions/{created.SessionId:D}/start",
            new SessionCommandRequest(Guid.NewGuid(), created.Revision));
        startResponse.EnsureSuccessStatusCode();

        SimulationSessionSnapshot snapshot = await WaitForCompletionAsync(session);
        int[] cellIds = await finalFrameTask.WaitAsync(TimeSpan.FromSeconds(10));
        using HttpResponseMessage stateResponse = await http.GetAsync($"/api/sessions/{created.SessionId:D}");
        stateResponse.EnsureSuccessStatusCode();
        SessionDto finalState = (await stateResponse.Content.ReadFromJsonAsync<SessionDto>())!;
        Assert.Equal(SimulationSessionStatus.Completed, finalState.Status);

        return new ClosureResult(cellIds, snapshot, session.Metadata.RunIdentity, RecordingFrameCount: 0);
    }

    private static async Task StartAsync(SimulationSession session)
    {
        SimulationCommandResult result = await session.StartAsync(Guid.NewGuid(), expectedRevision: 0);
        Assert.Equal(SimulationCommandDisposition.Applied, result.Disposition);
    }

    private static async Task<SimulationSessionSnapshot> WaitForCompletionAsync(SimulationSession session)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            SimulationSessionSnapshot snapshot = session.GetSnapshot();
            if (snapshot.Status == SimulationSessionStatus.Completed)
                return snapshot;
            if (snapshot.Status is SimulationSessionStatus.Failed or SimulationSessionStatus.Interrupted or SimulationSessionStatus.Cancelled)
                throw new InvalidOperationException($"The closure session ended as {snapshot.Status}: {snapshot.Failure}");
            await Task.Delay(2);
        }
        throw new TimeoutException("The closure session did not complete in 30 seconds.");
    }

    private static async Task<SimulationSessionSnapshot> WaitForRecordingCompletionAsync(SimulationSession session)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            SimulationSessionSnapshot snapshot = session.GetSnapshot();
            if (snapshot.RecordingState == RecordingState.Completed)
                return snapshot;
            if (snapshot.RecordingState == RecordingState.Failed)
                throw new InvalidOperationException($"The closure recording failed: {snapshot.RecordingFailure}");
            await Task.Delay(2);
        }
        throw new TimeoutException("The closure recording did not complete in 30 seconds.");
    }

    private static async Task<int[]> ReadLatestFrameAsync(SimulationSession session)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await using IAsyncEnumerator<SimulationFrameLease> frames = session.WatchFramesAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await frames.MoveNextAsync().AsTask().WaitAsync(timeout.Token));
        using SimulationFrameLease lease = frames.Current;
        Assert.Equal(session.Metadata.GridWidth, lease.Width);
        Assert.Equal(session.Metadata.GridHeight, lease.Height);
        Assert.Equal(session.GetSnapshot().CurrentMcs, lease.Mcs);
        return lease.CellIds.ToArray();
    }

    private static async Task<int[]> ReadFrameAtMcsAsync(
        IAsyncEnumerator<SimulationFrameLease> frames,
        long expectedMcs,
        CancellationToken cancellationToken)
    {
        while (await frames.MoveNextAsync().AsTask().WaitAsync(cancellationToken))
        {
            using SimulationFrameLease lease = frames.Current;
            if (lease.Mcs == expectedMcs)
                return lease.CellIds.ToArray();
            if (lease.Mcs > expectedMcs)
                throw new InvalidOperationException($"The frame stream advanced beyond expected MCS {expectedMcs} to {lease.Mcs}.");
        }

        throw new InvalidOperationException($"The frame stream ended before MCS {expectedMcs} was published.");
    }

    private static void AssertMatches(ExperimentReplicateResult expected, int[] expectedCellIds, ClosureResult actual)
    {
        Assert.Equal(SimulationSessionStatus.Completed, actual.Snapshot.Status);
        Assert.Equal(expected.ExperimentId, actual.RunIdentity.ExperimentId);
        Assert.Equal(expected.ReplicateId, actual.RunIdentity.ReplicateId);
        Assert.Equal(expected.ReplicateIndex, actual.RunIdentity.ReplicateIndex);
        Assert.Equal(expected.Seed, actual.RunIdentity.ReplicateSeed);
        Assert.Equal(expected.InitialisationSeed, actual.RunIdentity.InitialisationSeed);
        Assert.Equal(expected.DynamicsSeed, actual.RunIdentity.DynamicsSeed);
        Assert.Equal(expected.Snapshots.Max(snapshot => snapshot.Mcs), actual.Snapshot.CurrentMcs);
        Assert.Equal(expected.AttemptCount, actual.Snapshot.Counters.Attempts);
        Assert.Equal(expected.AcceptedAttemptCount, actual.Snapshot.Counters.Accepted);
        Assert.Equal(expected.RejectedAttemptCount, actual.Snapshot.Counters.Rejected);
        Assert.Equal(expected.NoOpAttemptCount, actual.Snapshot.Counters.NoOps);
        Assert.Equal(expected.ConnectivityFallbackCount, actual.Snapshot.Counters.ConnectivityFallbacks);
        Assert.Equal(JsonSerializer.Serialize(expected.FinalMeasurements),
            JsonSerializer.Serialize(actual.Snapshot.LatestMeasurement?.Metrics));
        Assert.Equal(expectedCellIds, actual.CellIds);
    }

    private static string Format(ExperimentReplicateResult result) =>
        $"MCS={result.Snapshots.Max(snapshot => snapshot.Mcs)}, attempts={result.AttemptCount}, accepted={result.AcceptedAttemptCount}, rejected={result.RejectedAttemptCount}, no-ops={result.NoOpAttemptCount}, fallbacks={result.ConnectivityFallbackCount}";

    private static string Format(ClosureResult result)
    {
        SimulationOperationalCounters counters = result.Snapshot.Counters;
        return $"MCS={result.Snapshot.CurrentMcs}, attempts={counters.Attempts}, accepted={counters.Accepted}, rejected={counters.Rejected}, no-ops={counters.NoOps}, fallbacks={counters.ConnectivityFallbacks}";
    }

    private static string HashCellIds(int[] cellIds)
    {
        byte[] bytes = new byte[checked(cellIds.Length * sizeof(int))];
        for (int index = 0; index < cellIds.Length; index++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int)), cellIds[index]);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed record ClosureResult(
        int[] CellIds,
        SimulationSessionSnapshot Snapshot,
        SimulationRunIdentity RunIdentity,
        int RecordingFrameCount);

    private sealed class ProbeRecordingStore : IRecordingStore
    {
        private Guid _sessionId;
        private int _frameCount;

        public int FrameCount => Volatile.Read(ref _frameCount);

        public RecordingCompletion? Completion { get; private set; }

        public ValueTask CreateAsync(RecordingHeader header, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _sessionId = header.SessionId;
            return ValueTask.CompletedTask;
        }

        public ValueTask AppendFrameAsync(Guid sessionId, EncodedRecordingFrame frame, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sessionId != _sessionId)
                throw new InvalidOperationException("The closure frame belongs to a different session.");
            Interlocked.Increment(ref _frameCount);
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(Guid sessionId, RecordingCompletion completion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sessionId != _sessionId)
                throw new InvalidOperationException("The closure completion belongs to a different session.");
            Completion = completion;
            return ValueTask.CompletedTask;
        }
    }
}
