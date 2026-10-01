namespace Rowles.Evolution.Metrics;

/// <summary>Progress at a completed ask/tell boundary. QD score is a signed sum relative to an explicit baseline.</summary>
public sealed record QdProgressMetrics(long Iteration, long EvaluationsPerformed, int ArchiveOccupancy,
    double ArchiveCoverage, double? QdScore, double? BestObjective, long ArchiveInsertions,
    long ArchiveReplacements, long InvalidEvaluations, IReadOnlyDictionary<string, long> FailureCounts);
