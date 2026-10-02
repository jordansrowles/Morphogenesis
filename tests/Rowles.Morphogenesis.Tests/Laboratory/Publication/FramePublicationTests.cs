using System.Runtime.InteropServices;
using Rowles.Morphogenesis.Laboratory.Publication;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Tests.Laboratory;

namespace Rowles.Morphogenesis.Tests.Laboratory.Publication;

public sealed class FramePublicationTests
{
    [Fact]
    public void PoolAllocatesTenDistinctBuffersAndReusesReturnedStorage()
    {
        using FrameBufferPool pool = new(bufferCount: 10, siteCount: 16);
        FrameBufferOwner[] owners = new FrameBufferOwner[10];
        HashSet<int[]> arrays = new(ReferenceEqualityComparer.Instance);

        for (int index = 0; index < owners.Length; index++)
        {
            Assert.True(pool.TryAcquire(out FrameBufferOwner? owner));
            owners[index] = owner!;
            Assert.True(arrays.Add(owner!.CellIds));
        }

        Assert.False(pool.TryAcquire(out _));
        int[] returnedArray = owners[0].CellIds;
        owners[0].Release();
        Assert.True(pool.TryAcquire(out FrameBufferOwner? reused));
        Assert.Same(returnedArray, reused!.CellIds);
        reused.Release();

        for (int index = 1; index < owners.Length; index++)
        {
            owners[index].Release();
        }
    }

    [Fact]
    public async Task SlowSubscriberReceivesLatestFrameAfterReleasingItsHeldLease()
    {
        using FrameBufferPool pool = new(bufferCount: 10, siteCount: 4);
        int drops = 0;
        using LatestFrameHub hub = new(Guid.NewGuid(), 2, 2, 1, () => drops++);
        await using IAsyncEnumerator<SimulationFrameLease> subscriber = hub.Watch().GetAsyncEnumerator();
        Publish(pool, hub, sequence: 1, mcs: 0);

        Assert.True(await subscriber.MoveNextAsync());
        SimulationFrameLease held = subscriber.Current;
        Publish(pool, hub, sequence: 2, mcs: 1);
        Publish(pool, hub, sequence: 3, mcs: 2);
        Assert.Equal(2, drops);

        held.Dispose();
        Assert.True(await subscriber.MoveNextAsync());
        Assert.Equal(3, subscriber.Current.Sequence);
        Assert.Equal(2, subscriber.Current.Mcs);
        subscriber.Current.Dispose();
    }

    [Fact]
    public async Task QueuedFrameIsReplacedAndNewSubscriberReceivesAuthoritativeLatest()
    {
        using FrameBufferPool pool = new(bufferCount: 10, siteCount: 4);
        int drops = 0;
        using LatestFrameHub hub = new(Guid.NewGuid(), 2, 2, 1, () => drops++);
        Publish(pool, hub, sequence: 1, mcs: 0);
        await using IAsyncEnumerator<SimulationFrameLease> subscriber = hub.Watch().GetAsyncEnumerator();
        Publish(pool, hub, sequence: 2, mcs: 4);

        Assert.Equal(1, drops);
        Assert.True(await subscriber.MoveNextAsync());
        Assert.Equal(2, subscriber.Current.Sequence);
        Assert.Equal(4, subscriber.Current.Mcs);
        subscriber.Current.Dispose();

        await subscriber.DisposeAsync();
        await using IAsyncEnumerator<SimulationFrameLease> reconnect = hub.Watch().GetAsyncEnumerator();
        Assert.True(await reconnect.MoveNextAsync());
        Assert.Equal(2, reconnect.Current.Sequence);
        Assert.Equal(4, reconnect.Current.Mcs);
    }

    [Fact]
    public async Task NinthSubscriberIsRejectedAndUnsubscribeReturnsAllBuffers()
    {
        using FrameBufferPool pool = new(bufferCount: 10, siteCount: 4);
        using LatestFrameHub hub = new(Guid.NewGuid(), 2, 2, 8, static () => { });
        Publish(pool, hub, sequence: 1, mcs: 0);
        IAsyncEnumerator<SimulationFrameLease>[] subscribers = Enumerable.Range(0, 8)
            .Select(_ => hub.Watch().GetAsyncEnumerator())
            .ToArray();

        Assert.Throws<InvalidOperationException>(() => hub.Watch().GetAsyncEnumerator());
        foreach (IAsyncEnumerator<SimulationFrameLease> subscriber in subscribers)
        {
            await subscriber.DisposeAsync();
        }

        hub.Dispose();
        FrameBufferOwner[] owners = new FrameBufferOwner[10];
        for (int index = 0; index < owners.Length; index++)
        {
            Assert.True(pool.TryAcquire(out FrameBufferOwner? owner));
            owners[index] = owner!;
        }

        Assert.False(pool.TryAcquire(out _));
        foreach (FrameBufferOwner owner in owners)
        {
            owner.Release();
        }
    }

    [Fact]
    public async Task SessionDisposalClosesSubscriptionsAndDisposesOutstandingLeases()
    {
        SimulationSession session = SimulationSessionFactory.Create(
            SessionTestFixture.CreateManifest(), replicateIndex: 0);
        IAsyncEnumerator<SimulationFrameLease> subscriber = session.WatchFramesAsync().GetAsyncEnumerator();
        Assert.True(await subscriber.MoveNextAsync());
        SimulationFrameLease lease = subscriber.Current;

        await session.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => _ = lease.CellIds);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await session.WatchFramesAsync().GetAsyncEnumerator().MoveNextAsync();
        });
        await subscriber.DisposeAsync();
    }

    [Fact]
    public async Task HeldLeaseKeepsItsBufferUnavailableUntilItIsDisposed()
    {
        using FrameBufferPool pool = new(bufferCount: 10, siteCount: 4);
        using LatestFrameHub hub = new(Guid.NewGuid(), 2, 2, 1, static () => { });
        await using IAsyncEnumerator<SimulationFrameLease> subscriber = hub.Watch().GetAsyncEnumerator();
        Publish(pool, hub, sequence: 1, mcs: 0);
        Assert.True(await subscriber.MoveNextAsync());
        SimulationFrameLease held = subscriber.Current;
        Assert.True(MemoryMarshal.TryGetArray(held.CellIds, out ArraySegment<int> heldArray));

        Publish(pool, hub, sequence: 2, mcs: 1);
        List<FrameBufferOwner> acquired = [];
        while (pool.TryAcquire(out FrameBufferOwner? owner))
        {
            acquired.Add(owner!);
        }

        Assert.Equal(8, acquired.Count);
        Assert.DoesNotContain(acquired, owner => ReferenceEquals(owner.CellIds, heldArray.Array));
        foreach (FrameBufferOwner owner in acquired)
        {
            owner.Release();
        }

        held.Dispose();
        Assert.True(pool.TryAcquire(out FrameBufferOwner? returned));
        Assert.Same(heldArray.Array, returned!.CellIds);
        Assert.Throws<ObjectDisposedException>(() => _ = held.CellIds);
        returned.Release();
    }

    [Fact]
    public async Task RepeatedSessionPublicationsReuseTheTenPreallocatedFullFrameArrays()
    {
        await using SimulationSession session = SimulationSessionFactory.Create(
            SessionTestFixture.CreateManifest(mcsCount: 12), replicateIndex: 0);
        await using IAsyncEnumerator<SimulationFrameLease> subscriber = session.WatchFramesAsync().GetAsyncEnumerator();
        HashSet<int[]> backingArrays = new(ReferenceEqualityComparer.Instance);
        Assert.True(await subscriber.MoveNextAsync());
        RecordPublishedArray(subscriber.Current, backingArrays);
        subscriber.Current.Dispose();

        long revision = 0;
        for (int mcs = 1; mcs <= 12; mcs++)
        {
            SimulationCommandResult step = await session.StepAsync(Guid.NewGuid(), revision);
            revision = step.Revision;
            Assert.True(await subscriber.MoveNextAsync());
            Assert.Equal(mcs, subscriber.Current.Mcs);
            RecordPublishedArray(subscriber.Current, backingArrays);
            subscriber.Current.Dispose();
        }

        Assert.InRange(backingArrays.Count, 1, 10);
        Assert.All(backingArrays, array => Assert.Equal(16 * 16, array.Length));
    }

    private static void Publish(FrameBufferPool pool, LatestFrameHub hub, long sequence, long mcs)
    {
        Assert.True(pool.TryAcquire(out FrameBufferOwner? owner));
        Array.Fill(owner!.CellIds, checked((int)mcs));
        try
        {
            Assert.True(hub.Publish(owner, sequence, mcs));
        }
        finally
        {
            owner.Release();
        }
    }

    private static void RecordPublishedArray(SimulationFrameLease lease, HashSet<int[]> backingArrays)
    {
        Assert.True(MemoryMarshal.TryGetArray(lease.CellIds, out ArraySegment<int> segment));
        Assert.NotNull(segment.Array);
        backingArrays.Add(segment.Array);
    }
}
