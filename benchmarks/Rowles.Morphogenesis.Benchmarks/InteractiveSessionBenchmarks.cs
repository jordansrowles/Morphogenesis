using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Laboratory.Publication;
using Rowles.Morphogenesis.Laboratory.Sessions;

namespace Rowles.Morphogenesis.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class InteractiveSessionWorkerBenchmarks
{
    private static readonly ExperimentManifest SessionManifest = CreateSessionManifest();

    [Params(5, 10, 20)]
    public int LivePublishMaxFps { get; set; }

    [Params(0, 1, 8)]
    public int SubscriberCount { get; set; }

    [Benchmark]
    public async Task<SimulationOperationalCounters> RunSessionToCompletion()
    {
        await using SimulationSession session = SimulationSessionFactory.Create(
            SessionManifest,
            replicateIndex: 0,
            options: new SimulationSessionOptions { LivePublishMaxFps = LivePublishMaxFps });
        Task[] consumers = Enumerable.Range(0, SubscriberCount)
            .Select(_ => ConsumeFramesAsync(session))
            .ToArray();

        try
        {
            await session.StartAsync(Guid.NewGuid(), expectedRevision: 0).ConfigureAwait(false);
            SimulationSessionSnapshot completed = await WaitForCompletionAsync(session).ConfigureAwait(false);
            return completed.Counters;
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
            await Task.WhenAll(consumers).ConfigureAwait(false);
        }
    }

    private static ExperimentManifest CreateSessionManifest()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Scenarios", "E02-control.json");
        ExperimentManifest source = ExperimentManifest.ReadJson(path);
        return source with
        {
            GridWidth = 128,
            GridHeight = 128,
            McsCount = 256,
            ReplicateCount = 1,
            Measurements = source.Measurements with
            {
                EveryMcs = 64,
                IncludeMcsZero = false,
                ValidateInvariantsEveryMcs = 0
            }
        };
    }

    private static async Task ConsumeFramesAsync(SimulationSession session)
    {
        await using IAsyncEnumerator<SimulationFrameLease> frames =
            session.WatchFramesAsync().GetAsyncEnumerator();
        while (await frames.MoveNextAsync().ConfigureAwait(false))
        {
            frames.Current.Dispose();
        }
    }

    private static async Task<SimulationSessionSnapshot> WaitForCompletionAsync(SimulationSession session)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        await using IAsyncEnumerator<SimulationSessionSnapshot> states =
            session.WatchStateAsync(timeout.Token).GetAsyncEnumerator();
        while (await states.MoveNextAsync().ConfigureAwait(false))
        {
            if (states.Current.Status == SimulationSessionStatus.Completed)
            {
                return states.Current;
            }
        }

        throw new TimeoutException("The benchmark session ended before normal completion.");
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class SlowInteractiveSubscriberBenchmarks
{
    private FrameBufferPool _pool = null!;
    private LatestFrameHub _hub = null!;
    private IAsyncEnumerator<SimulationFrameLease> _subscriber = null!;
    private long _sequence;
    private long _coalescedOrDropped;

    [GlobalSetup]
    public void CreatePublisher()
    {
        _pool = new FrameBufferPool(bufferCount: 10, siteCount: 1);
        _hub = new LatestFrameHub(Guid.NewGuid(), 1, 1, 1, () => _coalescedOrDropped++);
        PublishFrame();
    }

    [IterationSetup]
    public void HoldLatestFrame()
    {
        _subscriber = _hub.Watch().GetAsyncEnumerator();
        _subscriber.MoveNextAsync().AsTask().GetAwaiter().GetResult();
    }

    [Benchmark]
    public long ReplaceLatestWhileSubscriberIsSlow()
    {
        long before = _coalescedOrDropped;
        for (int index = 0; index < 1_200_000; index++)
        {
            PublishFrame();
        }

        return _coalescedOrDropped - before;
    }

    [IterationCleanup]
    public void ReleaseSubscriber()
    {
        _subscriber.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void DisposePublisher()
    {
        _hub.Dispose();
        _pool.Dispose();
    }

    private void PublishFrame()
    {
        if (!_pool.TryAcquire(out FrameBufferOwner? owner) || owner is null)
        {
            throw new InvalidOperationException("The slow-subscriber benchmark exhausted its frame buffers.");
        }

        owner.CellIds[0] = checked((int)++_sequence);
        try
        {
            if (!_hub.Publish(owner, _sequence, _sequence))
            {
                throw new ObjectDisposedException(nameof(LatestFrameHub));
            }
        }
        finally
        {
            owner.Release();
        }
    }
}
