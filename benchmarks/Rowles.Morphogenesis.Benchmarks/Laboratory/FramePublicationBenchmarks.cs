using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Laboratory.Publication;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Lattice;

namespace Rowles.Morphogenesis.Benchmarks.Laboratory;

[MemoryDiagnoser]
[ShortRunJob]
public class FramePublicationBenchmarks
{
    private static readonly double[,] Contacts = { { 0, 4 }, { 4, 0 } };
    private MorphogenesisState _state = null!;
    private FrameBufferPool _pool = null!;
    private LatestFrameHub _hub = null!;
    private IAsyncEnumerator<SimulationFrameLease>[] _subscribers = [];
    private long _sequence;

    [Params(256, 512)]
    public int Width { get; set; }

    [Params(0, 1, 8)]
    public int SubscriberCount { get; set; }

    [GlobalSetup]
    public async Task CreatePublicationPath()
    {
        int[] cellIds = new int[checked(Width * Width)];
        int side = Width / 4;
        int start = (Width - side) / 2;
        for (int y = start; y < start + side; y++)
        {
            for (int x = start; x < start + side; x++)
                cellIds[y * Width + x] = 1;
        }

        _state = new MorphogenesisState(
            Width,
            Width,
            cellIds,
            [new CellDefinition(1, 1, side * side, 0.5, 0, 0)],
            new ContactEnergyMatrix(Contacts),
            SimulationConfiguration.WallCanonical);
        _pool = new FrameBufferPool(bufferCount: 10, siteCount: _state.SiteCount);
        _hub = new LatestFrameHub(Guid.NewGuid(), Width, Width, maximumSubscribers: 8, static () => { });
        _subscribers = Enumerable.Range(0, SubscriberCount)
            .Select(_ => _hub.Watch().GetAsyncEnumerator())
            .ToArray();

        if (_subscribers.Length > 0)
        {
            Task<bool>[] initialReads = _subscribers
                .Select(subscriber => subscriber.MoveNextAsync().AsTask())
                .ToArray();
            _ = PublishOneFrame();
            foreach ((IAsyncEnumerator<SimulationFrameLease> subscriber, Task<bool> initialRead) in _subscribers.Zip(initialReads))
            {
                if (!await initialRead.ConfigureAwait(false))
                    throw new InvalidOperationException("A benchmark frame subscriber was closed during setup.");
                subscriber.Current.Dispose();
            }
        }
    }

    [Benchmark]
    public long CopyAndPublishOneFrame() => PublishOneFrame();

    private long PublishOneFrame()
    {
        if (!_pool.TryAcquire(out FrameBufferOwner? owner) || owner is null)
            throw new InvalidOperationException("The bounded frame buffer pool was exhausted.");

        long sequence = ++_sequence;
        try
        {
            _state.CopyCellIdsTo(owner.CellIds);
            if (!_hub.Publish(owner, sequence, sequence))
                throw new ObjectDisposedException(nameof(LatestFrameHub));
        }
        finally
        {
            owner.Release();
        }

        return sequence;
    }

    [GlobalCleanup]
    public async Task DisposePublicationPath()
    {
        foreach (IAsyncEnumerator<SimulationFrameLease> subscriber in _subscribers)
            await subscriber.DisposeAsync().ConfigureAwait(false);
        _hub.Dispose();
        _pool.Dispose();
    }
}
