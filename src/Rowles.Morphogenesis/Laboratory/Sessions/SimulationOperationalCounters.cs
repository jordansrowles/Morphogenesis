namespace Rowles.Morphogenesis.Laboratory.Sessions;

public sealed record SimulationOperationalCounters(
    TimeSpan Elapsed,
    long CurrentMcs,
    long Attempts,
    long Accepted,
    long Rejected,
    long NoOps,
    long ConnectivityFallbacks,
    long PublishedFrames,
    long CoalescedOrDroppedFrames,
    long FrameCaptureTicks,
    long CapturedFrames = 0);
