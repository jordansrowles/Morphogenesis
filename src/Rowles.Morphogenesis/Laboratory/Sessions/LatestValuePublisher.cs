using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Rowles.Morphogenesis.Laboratory.Sessions;

internal sealed class LatestValuePublisher<T> : IDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<Channel<T>> _watchers = [];
    private readonly Func<T, T> _clone;
    private bool _hasLatest;
    private T _latest = default!;
    private bool _disposed;

    internal LatestValuePublisher(Func<T, T>? clone = null)
    {
        _clone = clone ?? (static value => value);
    }

    internal IAsyncEnumerable<T> Watch(CancellationToken cancellationToken = default) =>
        WatchCore(cancellationToken);

    internal void Publish(T value)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _latest = _clone(value);
            _hasLatest = true;
            foreach (Channel<T> watcher in _watchers)
            {
                watcher.Writer.TryWrite(_clone(_latest));
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (Channel<T> watcher in _watchers)
            {
                watcher.Writer.TryComplete();
            }

            _watchers.Clear();
        }
    }

    private async IAsyncEnumerable<T> WatchCore(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Channel<T> watcher;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            watcher = Channel.CreateBounded<T>(new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = false
            });
            if (_hasLatest)
            {
                watcher.Writer.TryWrite(_clone(_latest));
            }

            _watchers.Add(watcher);
        }

        try
        {
            await foreach (T item in watcher.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            lock (_gate)
            {
                _watchers.Remove(watcher);
                watcher.Writer.TryComplete();
            }
        }
    }
}
