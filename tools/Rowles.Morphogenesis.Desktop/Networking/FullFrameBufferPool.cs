namespace Rowles.Morphogenesis.Desktop.Networking;

internal sealed class FullFrameBufferPool
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _bufferSlots;
    private readonly Dictionary<int, Stack<int[]>> _available = [];

    internal FullFrameBufferPool(int maximumOutstandingBuffers)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOutstandingBuffers);
        _bufferSlots = new SemaphoreSlim(maximumOutstandingBuffers, maximumOutstandingBuffers);
    }

    internal async ValueTask<FullFrameBuffer> RentAsync(int siteCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(siteCount);
        await _bufferSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        int[]? cellIds = null;
        try
        {
            lock (_gate)
            {
                if (_available.TryGetValue(siteCount, out Stack<int[]>? buffers) && buffers.TryPop(out int[]? reused))
                    cellIds = reused;
            }

            cellIds ??= new int[siteCount];
            return new FullFrameBuffer(cellIds, this);
        }
        catch
        {
            if (cellIds is not null)
                Return(cellIds);
            else
                _bufferSlots.Release();
            throw;
        }
    }

    internal void Return(int[] cellIds)
    {
        lock (_gate)
        {
            if (!_available.TryGetValue(cellIds.Length, out Stack<int[]>? buffers))
            {
                buffers = new Stack<int[]>();
                _available.Add(cellIds.Length, buffers);
            }

            buffers.Push(cellIds);
        }

        _bufferSlots.Release();
    }
}
