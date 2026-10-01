using Rowles.Morphogenesis.Measurements;
using Rowles.Morphogenesis.Snapshots;

namespace Rowles.Morphogenesis.Experiments.Results;

public sealed record ExperimentReplicateResult(
    string ExperimentId,
    string ReplicateId,
    int ReplicateIndex,
    ulong Seed,
    ulong InitialisationSeed,
    ulong DynamicsSeed,
    ReplicateRunStatus Status,
    string? Failure,
    MeasurementSample[] Samples,
    TissueMeasurements? FinalMeasurements,
    ExperimentSnapshot[] Snapshots,
    long AttemptCount,
    long AcceptedAttemptCount,
    long RejectedAttemptCount,
    long NoOpAttemptCount,
    long ConnectivityFallbackCount,
    double ElapsedMilliseconds,
    long AllocatedBytes,
    double MeasurementMilliseconds);
