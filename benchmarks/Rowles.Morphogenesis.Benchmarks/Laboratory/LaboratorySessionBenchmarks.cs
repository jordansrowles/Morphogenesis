using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Execution;
using Rowles.Morphogenesis.Laboratory.Publication;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;

namespace Rowles.Morphogenesis.Benchmarks.Laboratory;

[MemoryDiagnoser]
[MediumRunJob]
public class LaboratoryKernelBenchmarks
{
    private M3BenchmarkScenario _scenario = null!;
    private ExperimentSimulationInstance _withoutSink = null!;
    private ExperimentSimulationInstance _withPassiveSink = null!;
    private ExperimentSimulationInstance _withCoalescingSink = null!;
    private CoalescingLatticeChangeAccumulator _accumulator = null!;

    public IEnumerable<string> WorkloadIds =>
    [
        "B00-sparse-single-cell",
        "B01-e02-control",
        "B02-e02-sorting",
        "B03-high-fluctuation",
        "B05-scale-64",
        "B05-scale-128",
        "B05-scale-256",
        "B06-wall-control",
        "B05-interactive-512"
    ];

    [ParamsSource(nameof(WorkloadIds))]
    public string WorkloadId { get; set; } = "B02-e02-sorting";

    [IterationSetup]
    public void CreateSimulationControls()
    {
        _scenario = WorkloadId == M3BenchmarkScenario.Interactive512.Id
            ? M3BenchmarkScenario.Interactive512
            : M3BenchmarkScenario.Get(WorkloadId);
        _accumulator = new CoalescingLatticeChangeAccumulator(
            checked(_scenario.Manifest.GridWidth * _scenario.Manifest.GridHeight));
        _withoutSink = ExperimentSimulationFactory.Create(_scenario.Manifest, 0);
        _withPassiveSink = ExperimentSimulationFactory.Create(_scenario.Manifest, 0, PassiveMutationSink.Instance);
        _withCoalescingSink = ExperimentSimulationFactory.Create(_scenario.Manifest, 0, _accumulator);
    }

    [Benchmark(Baseline = true)]
    public McsSummary HeadlessCanonicalBaseline() => RunBatch(_withoutSink, McsPerInvocation, null);

    [Benchmark]
    public McsSummary HeadlessWithPassiveMutationSink() => RunBatch(_withPassiveSink, McsPerInvocation, null);

    [Benchmark]
    public McsSummary HeadlessWithRecordingDisabledCoalescingSink() =>
        RunBatch(_withCoalescingSink, McsPerInvocation, _accumulator);

    private int McsPerInvocation => WorkloadId == "B02-e02-sorting"
        ? 1_000
        : _scenario.KernelMcsPerInvocation;

    private static McsSummary RunBatch(
        ExperimentSimulationInstance instance,
        int mcsCount,
        CoalescingLatticeChangeAccumulator? accumulator)
    {
        McsSummary summary = default;
        for (int mcs = 0; mcs < mcsCount; mcs++)
        {
            summary = instance.Simulation.RunMcs();
            if (accumulator is not null && (mcs + 1) % 5 == 0)
                accumulator.Reset();
        }

        return summary;
    }

    private sealed class PassiveMutationSink : ILatticeMutationSink
    {
        internal static PassiveMutationSink Instance { get; } = new();

        public void AcceptedCopy(int targetIndex, int oldCellId, int newCellId)
        {
        }
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class LaboratorySessionBenchmarks
{
    private ExperimentManifest _manifest = null!;

    public IEnumerable<string> Profiles =>
    [
        "no-subscriber-256",
        "5fps-one-256",
        "10fps-one-256",
        "20fps-one-256",
        "10fps-eight-256",
        "recording-only-256",
        "recording-10fps-256",
        "10fps-one-512"
    ];

    [ParamsSource(nameof(Profiles))]
    public string Profile { get; set; } = "no-subscriber-256";

    [GlobalSetup]
    public void CreateManifest()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Scenarios", "E02-sorting.json");
        ExperimentManifest source = ExperimentManifest.ReadJson(path);
        int width = Profile.EndsWith("512", StringComparison.Ordinal) ? 512 : 256;
        _manifest = source with
        {
            GridWidth = width,
            GridHeight = width,
            McsCount = 100,
            ReplicateCount = 1,
            Initialiser = source.Initialiser with { CellCount = 1_024 },
            Measurements = source.Measurements with
            {
                EveryMcs = 100,
                IncludeMcsZero = false,
                ValidateInvariantsEveryMcs = 0
            }
        };
    }

    [Benchmark]
    public async Task<SimulationOperationalCounters> RunSessionToCompletion()
    {
        (int fps, int subscriberCount, bool recording) = Profile switch
        {
            "no-subscriber-256" => (10, 0, false),
            "5fps-one-256" => (5, 1, false),
            "10fps-one-256" => (10, 1, false),
            "20fps-one-256" => (20, 1, false),
            "10fps-eight-256" => (10, 8, false),
            "recording-only-256" => (10, 0, true),
            "recording-10fps-256" => (10, 1, true),
            "10fps-one-512" => (10, 1, false),
            _ => throw new ArgumentOutOfRangeException(nameof(Profile), Profile, "Unsupported laboratory benchmark profile.")
        };

        SimulationSession session = SimulationSessionFactory.Create(
            _manifest,
            replicateIndex: 0,
            options: new SimulationSessionOptions { LivePublishMaxFps = fps },
            recordingOptions: new RecordingOptions { Enabled = recording },
            recordingStore: recording ? BenchmarkRecordingStore.Instance : null);
        Task[] consumers = Enumerable.Range(0, subscriberCount)
            .Select(_ => ConsumeFramesAsync(session))
            .ToArray();
        try
        {
            _ = await session.StartAsync(Guid.NewGuid(), expectedRevision: 0).ConfigureAwait(false);
            SimulationSessionSnapshot completed = await WaitForCompletionAsync(session).ConfigureAwait(false);
            return completed.Counters;
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
            await Task.WhenAll(consumers).ConfigureAwait(false);
        }
    }

    private static async Task ConsumeFramesAsync(SimulationSession session)
    {
        await using IAsyncEnumerator<SimulationFrameLease> frames = session.WatchFramesAsync().GetAsyncEnumerator();
        while (await frames.MoveNextAsync().ConfigureAwait(false))
            frames.Current.Dispose();
    }

    private static async Task<SimulationSessionSnapshot> WaitForCompletionAsync(SimulationSession session)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(5));
        await using IAsyncEnumerator<SimulationSessionSnapshot> snapshots = session.WatchStateAsync(timeout.Token).GetAsyncEnumerator();
        while (await snapshots.MoveNextAsync().ConfigureAwait(false))
        {
            if (snapshots.Current.Status == SimulationSessionStatus.Completed)
                return snapshots.Current;
            if (snapshots.Current.Status is SimulationSessionStatus.Failed or SimulationSessionStatus.Cancelled)
                throw new InvalidOperationException($"Benchmark session ended as {snapshots.Current.Status}: {snapshots.Current.Failure}");
        }

        throw new TimeoutException("The benchmark session did not complete.");
    }

    private sealed class BenchmarkRecordingStore : IRecordingStore
    {
        internal static BenchmarkRecordingStore Instance { get; } = new();

        public ValueTask CreateAsync(RecordingHeader header, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask AppendFrameAsync(Guid sessionId, EncodedRecordingFrame frame, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask CompleteAsync(Guid sessionId, RecordingCompletion completion, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
