using Rowles.Morphogenesis.Experiments.Results;

namespace Rowles.Morphogenesis.Laboratory.Sessions;

public sealed record SimulationSessionSnapshot(
    Guid SessionId,
    long Revision,
    SimulationSessionStatus Status,
    long CurrentMcs,
    MeasurementSample? LatestMeasurement,
    SimulationOperationalCounters Counters,
    string? Failure);
