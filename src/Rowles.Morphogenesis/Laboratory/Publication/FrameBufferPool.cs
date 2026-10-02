namespace Rowles.Morphogenesis.Laboratory.Publication;

internal sealed class FrameBufferPool : IDisposable
{
    private readonly object _gate = new();
    private readonly Stack<FrameBufferOwner> _available;
    private bool _disposed;

    internal FrameBufferPool(int bufferCount, int siteCount)
    {
        _available = new Stack<FrameBufferOwner>(bufferCount);
        for (int index = 0; index < bufferCount; index++)
        {
            _available.Push(new FrameBufferOwner(this, new int[siteCount]));
        }
    }

    internal bool TryAcquire(out FrameBufferOwner? owner)
    {
        lock (_gate)
        {
            if (_disposed || !_available.TryPop(out owner))
            {
                owner = null;
                return false;
            }

            owner.BeginUse();
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _available.Clear();
        }
    }

    internal void Return(FrameBufferOwner owner)
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _available.Push(owner);
            }
        }
    }
}

internal sealed class FrameBufferOwner(FrameBufferPool pool, int[] cellIds)
{
    private int _referenceCount;

    internal int[] CellIds { get; } = cellIds;

    internal void BeginUse()
    {
        if (Interlocked.CompareExchange(ref _referenceCount, 1, 0) != 0)
        {
            throw new InvalidOperationException("A frame buffer was acquired while still owned.");
        }
    }

    internal void AddReference()
    {
        while (true)
        {
            int current = Volatile.Read(ref _referenceCount);
            if (current <= 0)
            {
                throw new ObjectDisposedException(nameof(FrameBufferOwner));
            }

            if (Interlocked.CompareExchange(ref _referenceCount, checked(current + 1), current) == current)
            {
                return;
            }
        }
    }

    internal void Release()
    {
        int remaining = Interlocked.Decrement(ref _referenceCount);
        if (remaining < 0)
        {
            throw new InvalidOperationException("A frame buffer reference was released more than once.");
        }

        if (remaining == 0)
        {
            pool.Return(this);
        }
    }
}
