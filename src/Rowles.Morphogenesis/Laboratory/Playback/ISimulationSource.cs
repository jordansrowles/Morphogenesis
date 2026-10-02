using Rowles.Morphogenesis.Laboratory.Publication;
using Rowles.Morphogenesis.Laboratory.Sessions;

namespace Rowles.Morphogenesis.Laboratory.Playback;

public interface ISimulationSource : IAsyncDisposable
{
    SimulationMetadata Metadata { get; }

    SimulationSourceCapabilities Capabilities { get; }

    long CurrentMcs { get; }

    ValueTask<SimulationFrameLease> GetCurrentFrameAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<SimulationFrameLease> WatchFramesAsync(CancellationToken cancellationToken = default);

    ValueTask PlayAsync(CancellationToken cancellationToken = default);

    ValueTask PauseAsync(CancellationToken cancellationToken = default);

    ValueTask SeekAsync(long mcs, CancellationToken cancellationToken = default);

    ValueTask StepForwardAsync(CancellationToken cancellationToken = default);

    ValueTask StepBackwardAsync(CancellationToken cancellationToken = default);
}
