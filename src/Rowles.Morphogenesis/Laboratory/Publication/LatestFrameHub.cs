using System.Threading.Channels;

namespace Rowles.Morphogenesis.Laboratory.Publication;

internal sealed class LatestFrameHub : IDisposable
{
    private readonly object _gate = new();
    private readonly Guid _sessionId;
    private readonly int _width;
    private readonly int _height;
    private readonly int _maximumSubscribers;
    private readonly Action _coalescedOrDropped;
    private readonly HashSet<FrameSubscriber> _subscribers = [];
    private FrameBufferOwner? _latestOwner;
    private long _latestSequence;
    private long _latestMcs;
    private bool _disposed;

    internal LatestFrameHub(
        Guid sessionId,
        int width,
        int height,
        int maximumSubscribers,
        Action coalescedOrDropped)
    {
        _sessionId = sessionId;
        _width = width;
        _height = height;
        _maximumSubscribers = maximumSubscribers;
        _coalescedOrDropped = coalescedOrDropped;
    }

    internal IAsyncEnumerable<SimulationFrameLease> Watch(CancellationToken cancellationToken = default) =>
        new FrameSubscriptionEnumerable(this, cancellationToken);

    internal long LatestMcs
    {
        get
        {
            lock (_gate)
            {
                return _latestMcs;
            }
        }
    }

    internal int SubscriberCount
    {
        get
        {
            lock (_gate)
                return _subscribers.Count;
        }
    }

    internal bool Publish(FrameBufferOwner owner, long sequence, long mcs)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            owner.AddReference();
            FrameBufferOwner? previous = _latestOwner;
            _latestOwner = owner;
            _latestSequence = sequence;
            _latestMcs = mcs;
            foreach (FrameSubscriber subscriber in _subscribers)
            {
                subscriber.SetLatest(owner, sequence, mcs);
            }

            previous?.Release();
            return true;
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
            foreach (FrameSubscriber subscriber in _subscribers)
            {
                subscriber.DisposeUnderLock();
            }

            _subscribers.Clear();
            _latestOwner?.Release();
            _latestOwner = null;
        }
    }

    private FrameSubscriber RegisterSubscriber()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_subscribers.Count >= _maximumSubscribers)
            {
                throw new InvalidOperationException($"A session supports at most {_maximumSubscribers} live frame subscribers.");
            }

            FrameSubscriber subscriber = new(this, _sessionId, _width, _height, _coalescedOrDropped);
            _subscribers.Add(subscriber);
            if (_latestOwner is not null)
            {
                subscriber.SetLatest(_latestOwner, _latestSequence, _latestMcs);
            }

            return subscriber;
        }
    }

    private void UnregisterSubscriber(FrameSubscriber subscriber)
    {
        lock (_gate)
        {
            if (_subscribers.Remove(subscriber))
            {
                subscriber.DisposeUnderLock();
            }
        }
    }

    private void LeaseDisposed(FrameSubscriber subscriber, SimulationFrameLease lease)
    {
        lock (_gate)
        {
            if (_subscribers.Contains(subscriber) && _latestOwner is not null)
            {
                subscriber.LeaseDisposedUnderLock(lease, _latestOwner, _latestSequence, _latestMcs);
            }
            else
            {
                subscriber.LeaseDisposedUnderLock(lease, null, _latestSequence, _latestMcs);
            }
        }
    }

    private sealed class FrameSubscriptionEnumerable(
        LatestFrameHub hub,
        CancellationToken subscriptionToken) : IAsyncEnumerable<SimulationFrameLease>
    {
        public IAsyncEnumerator<SimulationFrameLease> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            CancellationTokenSource? linkedSource = null;
            CancellationToken effectiveToken;
            if (subscriptionToken.CanBeCanceled && cancellationToken.CanBeCanceled && subscriptionToken != cancellationToken)
            {
                linkedSource = CancellationTokenSource.CreateLinkedTokenSource(subscriptionToken, cancellationToken);
                effectiveToken = linkedSource.Token;
            }
            else
            {
                effectiveToken = subscriptionToken.CanBeCanceled ? subscriptionToken : cancellationToken;
            }

            try
            {
                FrameSubscriber subscriber = hub.RegisterSubscriber();
                return new FrameSubscriptionEnumerator(hub, subscriber, effectiveToken, linkedSource);
            }
            catch
            {
                linkedSource?.Dispose();
                throw;
            }
        }
    }

    private sealed class FrameSubscriptionEnumerator(
        LatestFrameHub hub,
        FrameSubscriber subscriber,
        CancellationToken cancellationToken,
        CancellationTokenSource? linkedSource) : IAsyncEnumerator<SimulationFrameLease>
    {
        private bool _disposed;

        public SimulationFrameLease Current { get; private set; } = null!;

        public async ValueTask<bool> MoveNextAsync()
        {
            if (_disposed)
            {
                return false;
            }

            try
            {
                while (await subscriber.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (subscriber.Reader.TryRead(out SimulationFrameLease? lease))
                    {
                        Current = lease;
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                await DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                hub.UnregisterSubscriber(subscriber);
                linkedSource?.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }

    internal sealed class FrameSubscriber
    {
        private readonly LatestFrameHub _hub;
        private readonly Guid _sessionId;
        private readonly int _width;
        private readonly int _height;
        private readonly Action _coalescedOrDropped;
        private readonly Channel<SimulationFrameLease> _channel =
            Channel.CreateBounded<SimulationFrameLease>(new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        private SimulationFrameLease? _outstanding;
        private bool _disposed;

        internal FrameSubscriber(
            LatestFrameHub hub,
            Guid sessionId,
            int width,
            int height,
            Action coalescedOrDropped)
        {
            _hub = hub;
            _sessionId = sessionId;
            _width = width;
            _height = height;
            _coalescedOrDropped = coalescedOrDropped;
        }

        internal ChannelReader<SimulationFrameLease> Reader => _channel.Reader;

        internal void SetLatest(FrameBufferOwner owner, long sequence, long mcs)
        {
            if (_disposed)
            {
                return;
            }

            if (_outstanding is not null)
            {
                if (_channel.Reader.TryRead(out SimulationFrameLease? queued))
                {
                    _outstanding = null;
                    _coalescedOrDropped();
                    queued.Dispose();
                }
                else
                {
                    _coalescedOrDropped();
                    return;
                }
            }

            Enqueue(owner, sequence, mcs);
        }

        internal void LeaseDisposed(SimulationFrameLease lease) => _hub.LeaseDisposed(this, lease);

        internal void LeaseDisposedUnderLock(
            SimulationFrameLease lease,
            FrameBufferOwner? latestOwner,
            long latestSequence,
            long latestMcs)
        {
            if (!ReferenceEquals(_outstanding, lease))
            {
                return;
            }

            _outstanding = null;
            if (!_disposed && latestOwner is not null && latestSequence > lease.Sequence)
            {
                Enqueue(latestOwner, latestSequence, latestMcs);
            }
        }

        internal void DisposeUnderLock()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            SimulationFrameLease? outstanding = _outstanding;
            _outstanding = null;
            if (_channel.Reader.TryRead(out SimulationFrameLease? queued))
            {
                queued.Dispose();
            }

            outstanding?.Dispose();
            _channel.Writer.TryComplete();
        }

        private void Enqueue(FrameBufferOwner owner, long sequence, long mcs)
        {
            owner.AddReference();
            SimulationFrameLease lease = new(_sessionId, sequence, mcs, _width, _height, owner, this);
            _outstanding = lease;
            if (!_channel.Writer.TryWrite(lease))
            {
                _outstanding = null;
                _coalescedOrDropped();
                lease.Dispose();
            }
        }
    }
}
