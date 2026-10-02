namespace Rowles.Morphogenesis.Benchmarks.Analysis;

internal sealed record EnsembleQualificationResult(
    string Kernel,
    int SeedCount,
    int MeanMetricsEvaluated,
    int VarianceMetricsEvaluated,
    int MeanEquivalenceFailures,
    int VarianceRatioFailures,
    int ProvenanceFailures,
    int CheckpointZeroFailures,
    bool Passed,
    string[] FailureDescriptions);
