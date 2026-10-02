namespace Rowles.Morphogenesis.Laboratory.Publication;

public sealed class SimulationFrameLease : IDisposable
{
    private FrameBufferOwner? _owner;
    private LatestFrameHub.FrameSubscriber? _subscriber;

    internal SimulationFrameLease(
        Guid sessionId,
        long sequence,
        long mcs,
        int width,
        int height,
        FrameBufferOwner owner,
        LatestFrameHub.FrameSubscriber subscriber)
    {
        SessionId = sessionId;
        Sequence = sequence;
        Mcs = mcs;
        Width = width;
        Height = height;
        _owner = owner;
        _subscriber = subscriber;
    }

    internal SimulationFrameLease(
        Guid sessionId,
        long sequence,
        long mcs,
        int width,
        int height,
        FrameBufferOwner owner)
    {
        SessionId = sessionId;
        Sequence = sequence;
        Mcs = mcs;
        Width = width;
        Height = height;
        _owner = owner;
    }

    public Guid SessionId { get; }

    public long Sequence { get; }

    public long Mcs { get; }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyMemory<int> CellIds =>
        (Volatile.Read(ref _owner) ?? throw new ObjectDisposedException(nameof(SimulationFrameLease))).CellIds;

    public void Dispose()
    {
        FrameBufferOwner? owner = Interlocked.Exchange(ref _owner, null);
        if (owner is null)
        {
            return;
        }

        owner.Release();
        LatestFrameHub.FrameSubscriber? subscriber = Interlocked.Exchange(ref _subscriber, null);
        subscriber?.LeaseDisposed(this);
    }
}
