namespace Rowles.Morphogenesis.Laboratory.Playback;

public sealed record SimulationSourceCapabilities(
    bool IsLive,
    bool CanSeek,
    bool CanReverse,
    bool CanControlExecution,
    bool CanChangePlaybackRate);
