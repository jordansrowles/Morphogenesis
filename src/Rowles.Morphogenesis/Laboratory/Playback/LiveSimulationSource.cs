using Rowles.Morphogenesis.Laboratory.Publication;
using Rowles.Morphogenesis.Laboratory.Sessions;

namespace Rowles.Morphogenesis.Laboratory.Playback;

public sealed class LiveSimulationSource : ISimulationSource
{
    private static readonly SimulationSourceCapabilities SourceCapabilities = new(
        IsLive: true,
        CanSeek: false,
        CanReverse: false,
        CanControlExecution: true,
        CanChangePlaybackRate: false);
    private readonly SimulationSession _session;
    private readonly FrameBufferPool _singleFramePool;
    private int _disposed;

    public LiveSimulationSource(SimulationSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _singleFramePool = new FrameBufferPool(bufferCount: 10, checked(session.Metadata.GridWidth * session.Metadata.GridHeight));
    }

    public SimulationMetadata Metadata => _session.Metadata;

    public SimulationSourceCapabilities Capabilities => SourceCapabilities;

    public long CurrentMcs => _session.GetSnapshot().CurrentMcs;

    public async ValueTask<SimulationFrameLease> GetCurrentFrameAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await using IAsyncEnumerator<SimulationFrameLease> frames =
            _session.WatchFramesAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        if (!await frames.MoveNextAsync().ConfigureAwait(false))
        {
            throw new InvalidOperationException("The live session has not published an initial frame.");
        }

        SimulationFrameLease sourceFrame = frames.Current;
        if (!_singleFramePool.TryAcquire(out FrameBufferOwner? owner) || owner is null)
        {
            throw new InvalidOperationException("All live-source frame buffers are currently leased.");
        }

        try
        {
            sourceFrame.CellIds.Span.CopyTo(owner.CellIds);
            return new SimulationFrameLease(
                sourceFrame.SessionId,
                sourceFrame.Sequence,
                sourceFrame.Mcs,
                sourceFrame.Width,
                sourceFrame.Height,
                owner);
        }
        catch
        {
            owner.Release();
            throw;
        }
    }

    public IAsyncEnumerable<SimulationFrameLease> WatchFramesAsync(CancellationToken cancellationToken = default) =>
        _session.WatchFramesAsync(cancellationToken);

    public async ValueTask PlayAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        SimulationSessionSnapshot snapshot = _session.GetSnapshot();
        SimulationCommandResult? result = snapshot.Status switch
        {
            SimulationSessionStatus.Created => await _session.StartAsync(Guid.NewGuid(), snapshot.Revision, cancellationToken).ConfigureAwait(false),
            SimulationSessionStatus.Paused => await _session.ResumeAsync(Guid.NewGuid(), snapshot.Revision, cancellationToken).ConfigureAwait(false),
            SimulationSessionStatus.Running => null,
            _ => throw new InvalidOperationException($"A {snapshot.Status} session cannot be played.")
        };
        EnsureApplied(result);
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        SimulationSessionSnapshot snapshot = _session.GetSnapshot();
        if (snapshot.Status == SimulationSessionStatus.Paused)
        {
            return;
        }

        if (snapshot.Status != SimulationSessionStatus.Running)
        {
            throw new InvalidOperationException($"A {snapshot.Status} session cannot be paused.");
        }

        SimulationCommandResult result = await _session.PauseAsync(
            Guid.NewGuid(),
            snapshot.Revision,
            cancellationToken).ConfigureAwait(false);
        EnsureApplied(result);
    }

    public ValueTask SeekAsync(long mcs, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException("A live simulation cannot seek."));

    public async ValueTask StepForwardAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        SimulationSessionSnapshot snapshot = _session.GetSnapshot();
        if (snapshot.Status is not (SimulationSessionStatus.Created or SimulationSessionStatus.Paused))
        {
            throw new InvalidOperationException($"A {snapshot.Status} session cannot step while it is not paused.");
        }

        SimulationCommandResult result = await _session.StepAsync(
            Guid.NewGuid(),
            snapshot.Revision,
            cancellationToken).ConfigureAwait(false);
        EnsureApplied(result);
    }

    public ValueTask StepBackwardAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException("A live simulation cannot step backwards."));

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _singleFramePool.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private static void EnsureApplied(SimulationCommandResult? result)
    {
        if (result is not null && result.Disposition != SimulationCommandDisposition.Applied)
        {
            throw new InvalidOperationException($"The live session rejected the playback command: {result.Disposition}.");
        }
    }
}
