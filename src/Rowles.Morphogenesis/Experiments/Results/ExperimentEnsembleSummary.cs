using Rowles.Morphogenesis.Experiments.Random;

namespace Rowles.Morphogenesis.Experiments.Results;

public sealed record ExperimentEnsembleSummary(
    int AttemptedReplicates,
    int SuccessfulReplicates,
    int FailedReplicates,
    string FailurePolicy,
    Dictionary<string, MetricStatistics> FinalMetricStatistics)
{
    public static ExperimentEnsembleSummary Create(
        ExperimentManifest manifest,
        IReadOnlyList<ExperimentReplicateResult> replicates)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(replicates);
        manifest.Validate();
        if (replicates.Count != manifest.ReplicateCount)
        {
            throw new ArgumentException("Every configured replicate must remain in the ensemble result.", nameof(replicates));
        }

        bool[] seenIndices = new bool[manifest.ReplicateCount];
        foreach (ExperimentReplicateResult replicate in replicates)
        {
            if (replicate is null || replicate.ExperimentId != manifest.ExperimentId ||
                (uint)replicate.ReplicateIndex >= (uint)manifest.ReplicateCount || seenIndices[replicate.ReplicateIndex])
            {
                throw new ArgumentException("Experiment IDs and replicate indices must match the manifest and cover the ensemble.", nameof(replicates));
            }

            if (!Enum.IsDefined(replicate.Status) ||
                (replicate.Status == ReplicateRunStatus.Succeeded && replicate.FinalMeasurements is null) ||
                (replicate.Status == ReplicateRunStatus.Failed && string.IsNullOrWhiteSpace(replicate.Failure)))
            {
                throw new ArgumentException("Replicate status and final measurement/failure details are inconsistent.", nameof(replicates));
            }

            if (replicate.Seed != ReplicateSeedDerivation.Derive(manifest.BaseSeed, replicate.ReplicateIndex))
            {
                throw new ArgumentException("Replicate seed does not match the resolved manifest.", nameof(replicates));
            }

            if (replicate.InitialisationSeed != ExperimentSeedDerivation.DeriveInitialisation(replicate.Seed) ||
                replicate.DynamicsSeed != ExperimentSeedDerivation.DeriveDynamics(replicate.Seed))
            {
                throw new ArgumentException("Replicate initialisation or dynamics seed does not match the seed policy.", nameof(replicates));
            }

            seenIndices[replicate.ReplicateIndex] = true;
        }

        ExperimentReplicateResult[] successful = replicates
            .Where(result => result.Status == ReplicateRunStatus.Succeeded && result.FinalMeasurements is not null)
            .ToArray();
        Dictionary<string, MetricStatistics> statistics = new(StringComparer.Ordinal)
        {
            ["heterotypic-interface-fraction"] = MetricStatistics.From(successful.Select(result => result.FinalMeasurements!.HeterotypicInterfaceFraction)),
            ["heterotypic-interface-count"] = MetricStatistics.From(successful.Select(result => (double)result.FinalMeasurements!.HeterotypicInterfaceCount)),
            ["total-cell-cell-interface-count"] = MetricStatistics.From(successful.Select(result => (double)result.FinalMeasurements!.TotalCellCellInterfaceCount)),
            ["total-homotypic-interface-count"] = MetricStatistics.From(successful.Select(result =>
                (double)result.FinalMeasurements!.HomotypicInterfacesByType.Sum(count => count.Count))),
            ["accepted-attempt-fraction"] = MetricStatistics.From(successful.Select(result => result.AttemptCount == 0 ? 0 : (double)result.AcceptedAttemptCount / result.AttemptCount)),
            ["accepted-attempt-count"] = MetricStatistics.From(successful.Select(result => (double)result.AcceptedAttemptCount)),
            ["rejected-attempt-count"] = MetricStatistics.From(successful.Select(result => (double)result.RejectedAttemptCount)),
            ["noop-attempt-count"] = MetricStatistics.From(successful.Select(result => (double)result.NoOpAttemptCount)),
            ["type-a-cell-count"] = MetricStatistics.From(successful.Select(result => (double)result.FinalMeasurements!.TypeACellCount)),
            ["type-b-cell-count"] = MetricStatistics.From(successful.Select(result => (double)result.FinalMeasurements!.TypeBCellCount)),
            ["mean-cell-area"] = MetricStatistics.From(successful.Select(result => result.FinalMeasurements!.MeanCellArea)),
            ["mean-cell-perimeter"] = MetricStatistics.From(successful.Select(result => result.FinalMeasurements!.MeanCellPerimeter))
        };

        for (int typeId = 1; typeId < manifest.CellTypes.Length; typeId++)
        {
            int capturedTypeId = typeId;
            statistics[$"homotypic-interface-count.type-{typeId}"] = MetricStatistics.From(successful.Select(result =>
                (double)result.FinalMeasurements!.HomotypicInterfacesByType[capturedTypeId - 1].Count));
            statistics[$"type-domain-count.type-{typeId}"] = MetricStatistics.From(successful.Select(result =>
                (double)result.FinalMeasurements!.DomainsByType[capturedTypeId - 1].DomainCount));
        }

        int succeeded = successful.Length;
        return new ExperimentEnsembleSummary(
            replicates.Count,
            succeeded,
            replicates.Count - succeeded,
            "Metric statistics use successful replicates with a final measurement; attempted, successful, and failed counts remain explicit.",
            statistics);
    }
}
