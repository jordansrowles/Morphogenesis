using System.Diagnostics;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Laboratory.Recording;

namespace Rowles.Morphogenesis.Server.Diagnostics;

public sealed class LaboratoryDiagnostics
{
    private readonly object _rateGate = new();
    private long _recordingBytes;
    private long _recordingQueueSaturations;
    private long _commandConflicts;
    private long _commandFailures;
    private long _sqliteWriteTicks;
    private long _retiredMcs;
    private long _retiredAttempts;
    private long _retiredAccepted;
    private long _retiredRejected;
    private long _retiredNoOps;
    private long _retiredConnectivityFallbacks;
    private long _retiredCapturedFrames;
    private long _retiredPublishedFrames;
    private long _retiredCoalescedFrames;
    private long _retiredFrameCaptureTicks;
    private long _retiredRecordingFailures;
    private long _retiredRecordingQueueSaturations;
    private long _lastTimestamp;
    private RateCounters _lastCounters;

    public void RecordRecordingBytes(long bytes)
    {
        if (bytes > 0)
            Interlocked.Add(ref _recordingBytes, bytes);
    }

    public void RecordRecordingQueueSaturation() =>
        Interlocked.Increment(ref _recordingQueueSaturations);

    public void RecordCommandConflict() => Interlocked.Increment(ref _commandConflicts);

    public void RecordCommandFailure() => Interlocked.Increment(ref _commandFailures);

    public void RecordSqliteWrite(TimeSpan elapsed)
    {
        if (elapsed > TimeSpan.Zero)
            Interlocked.Add(ref _sqliteWriteTicks, elapsed.Ticks);
    }

    public void RecordRetiredSession(SimulationSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Interlocked.Add(ref _retiredMcs, snapshot.Counters.CurrentMcs);
        Interlocked.Add(ref _retiredAttempts, snapshot.Counters.Attempts);
        Interlocked.Add(ref _retiredAccepted, snapshot.Counters.Accepted);
        Interlocked.Add(ref _retiredRejected, snapshot.Counters.Rejected);
        Interlocked.Add(ref _retiredNoOps, snapshot.Counters.NoOps);
        Interlocked.Add(ref _retiredConnectivityFallbacks, snapshot.Counters.ConnectivityFallbacks);
        Interlocked.Add(ref _retiredCapturedFrames, snapshot.Counters.CapturedFrames);
        Interlocked.Add(ref _retiredPublishedFrames, snapshot.Counters.PublishedFrames);
        Interlocked.Add(ref _retiredCoalescedFrames, snapshot.Counters.CoalescedOrDroppedFrames);
        Interlocked.Add(ref _retiredFrameCaptureTicks, snapshot.Counters.FrameCaptureTicks);
        if (snapshot.RecordingState == RecordingState.Failed)
            Interlocked.Increment(ref _retiredRecordingFailures);
        if (snapshot.RecordingFailure?.Contains(nameof(RecordingBackpressureException), StringComparison.Ordinal) == true)
            Interlocked.Increment(ref _retiredRecordingQueueSaturations);
    }

    public LaboratoryDiagnosticsSnapshot Capture(
        IEnumerable<SimulationSession> sessions,
        long sqliteWriterQueueDepth)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        int activeSessions = 0;
        int runningSessions = 0;
        int pausedSessions = 0;
        int subscribers = 0;
        long totalMcs = Interlocked.Read(ref _retiredMcs);
        long attempts = Interlocked.Read(ref _retiredAttempts);
        long accepted = Interlocked.Read(ref _retiredAccepted);
        long rejected = Interlocked.Read(ref _retiredRejected);
        long noOps = Interlocked.Read(ref _retiredNoOps);
        long connectivityFallbacks = Interlocked.Read(ref _retiredConnectivityFallbacks);
        long capturedFrames = Interlocked.Read(ref _retiredCapturedFrames);
        long publishedFrames = Interlocked.Read(ref _retiredPublishedFrames);
        long coalescedFrames = Interlocked.Read(ref _retiredCoalescedFrames);
        long frameCaptureTicks = Interlocked.Read(ref _retiredFrameCaptureTicks);
        long recordingFailures = 0;
        long recordingQueueSaturations = Interlocked.Read(ref _retiredRecordingQueueSaturations);

        foreach (SimulationSession session in sessions)
        {
            SimulationSessionSnapshot snapshot = session.GetSnapshot();
            activeSessions++;
            runningSessions += snapshot.Status == SimulationSessionStatus.Running ? 1 : 0;
            pausedSessions += snapshot.Status == SimulationSessionStatus.Paused ? 1 : 0;
            subscribers += session.LiveSubscriberCount;
            totalMcs = checked(totalMcs + snapshot.Counters.CurrentMcs);
            attempts = checked(attempts + snapshot.Counters.Attempts);
            accepted = checked(accepted + snapshot.Counters.Accepted);
            rejected = checked(rejected + snapshot.Counters.Rejected);
            noOps = checked(noOps + snapshot.Counters.NoOps);
            connectivityFallbacks = checked(connectivityFallbacks + snapshot.Counters.ConnectivityFallbacks);
            capturedFrames = checked(capturedFrames + snapshot.Counters.CapturedFrames);
            publishedFrames = checked(publishedFrames + snapshot.Counters.PublishedFrames);
            coalescedFrames = checked(coalescedFrames + snapshot.Counters.CoalescedOrDroppedFrames);
            frameCaptureTicks = checked(frameCaptureTicks + snapshot.Counters.FrameCaptureTicks);
            recordingFailures += snapshot.RecordingState == RecordingState.Failed ? 1 : 0;
            if (snapshot.RecordingState == RecordingState.Failed &&
                snapshot.RecordingFailure?.Contains(nameof(RecordingBackpressureException), StringComparison.Ordinal) == true)
            {
                recordingQueueSaturations++;
            }
        }

        RateCounters current = new(
            totalMcs,
            attempts,
            accepted,
            rejected,
            noOps,
            Interlocked.Read(ref _recordingBytes));
        long timestamp = Stopwatch.GetTimestamp();
        double elapsedSeconds = 0;
        RateCounters previous;
        lock (_rateGate)
        {
            previous = _lastCounters;
            if (_lastTimestamp != 0 && timestamp > _lastTimestamp)
                elapsedSeconds = (timestamp - _lastTimestamp) / (double)Stopwatch.Frequency;
            _lastTimestamp = timestamp;
            _lastCounters = current;
        }

        double Rate(long currentValue, long previousValue) =>
            elapsedSeconds > 0 ? Math.Max(0, currentValue - previousValue) / elapsedSeconds : 0;

        using Process process = Process.GetCurrentProcess();
        return new LaboratoryDiagnosticsSnapshot(
            activeSessions,
            runningSessions,
            pausedSessions,
            subscribers,
            Rate(current.Mcs, previous.Mcs),
            Rate(current.Attempts, previous.Attempts),
            Rate(current.Accepted, previous.Accepted),
            Rate(current.Rejected, previous.Rejected),
            Rate(current.NoOps, previous.NoOps),
            connectivityFallbacks,
            capturedFrames,
            publishedFrames,
            coalescedFrames,
            frameCaptureTicks * 1000d / Stopwatch.Frequency,
            Rate(current.RecordingBytes, previous.RecordingBytes),
            checked(recordingFailures + Interlocked.Read(ref _retiredRecordingFailures)),
            Math.Max(recordingQueueSaturations, Interlocked.Read(ref _recordingQueueSaturations)),
            Interlocked.Read(ref _sqliteWriteTicks) / (double)TimeSpan.TicksPerMillisecond,
            sqliteWriterQueueDepth,
            Interlocked.Read(ref _commandConflicts),
            Interlocked.Read(ref _commandFailures),
            process.WorkingSet64,
            GC.GetTotalMemory(forceFullCollection: false),
            DateTimeOffset.UtcNow);
    }

    private readonly record struct RateCounters(
        long Mcs,
        long Attempts,
        long Accepted,
        long Rejected,
        long NoOps,
        long RecordingBytes);
}
