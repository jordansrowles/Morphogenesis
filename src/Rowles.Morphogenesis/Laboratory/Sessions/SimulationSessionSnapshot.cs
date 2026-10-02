using Rowles.Morphogenesis.Experiments.Results;
using Rowles.Morphogenesis.Laboratory.Recording;

namespace Rowles.Morphogenesis.Laboratory.Sessions;

public sealed record SimulationSessionSnapshot(
    Guid SessionId,
    long Revision,
    SimulationSessionStatus Status,
    long CurrentMcs,
    MeasurementSample? LatestMeasurement,
    SimulationOperationalCounters Counters,
    string? Failure,
    RecordingState RecordingState,
    string? RecordingFailure);
