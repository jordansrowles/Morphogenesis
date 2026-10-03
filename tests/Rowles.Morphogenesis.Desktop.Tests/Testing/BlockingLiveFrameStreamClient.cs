using System.Net.WebSockets;
using System.Threading.Channels;
using Rowles.Morphogenesis.Desktop.Networking;

namespace Rowles.Morphogenesis.Desktop.Tests.Testing;

internal sealed class BlockingLiveFrameStreamClient : ILiveFrameStreamClient
{
    private readonly Channel<int> _connections = Channel.CreateUnbounded<int>();
    private int _connectionCount;

    internal async Task WaitForConnectionsAsync(int count, CancellationToken cancellationToken = default)
    {
        while (Volatile.Read(ref _connectionCount) < count)
            await _connections.Reader.ReadAsync(cancellationToken);
    }

    public async Task StreamAsync(Uri streamUri, int width, int height,
        Func<FullFrameBuffer, CancellationToken, Task> onFrame, CancellationToken cancellationToken)
    {
        int connection = Interlocked.Increment(ref _connectionCount);
        _connections.Writer.TryWrite(connection);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }
}
