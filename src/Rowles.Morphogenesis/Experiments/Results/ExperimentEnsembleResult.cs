using Rowles.Morphogenesis.Serialisation;
using System.Text.Json;
using Rowles.Morphogenesis.Experiments.Random;
using Rowles.Morphogenesis.Measurements;
using Rowles.Morphogenesis.Measurements.Metrics;
using Rowles.Morphogenesis.Snapshots;

namespace Rowles.Morphogenesis.Experiments.Results;

public sealed record ExperimentEnsembleResult(
    ExperimentManifest Manifest,
    ExperimentRunMetadata Metadata,
    ExperimentRandomMetadata Random,
    ulong[] ReplicateSeeds,
    ExperimentReplicateResult[] Replicates,
    ExperimentEnsembleSummary Summary,
    double ElapsedMilliseconds,
    long AllocatedBytes)
{
    public string ToJson() => JsonSerializer.Serialize(this, MorphogenesisJsonContext.Instance.ExperimentEnsembleResult);

    public static ExperimentEnsembleResult FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        ExperimentEnsembleResult result = JsonSerializer.Deserialize(
            json,
            MorphogenesisJsonContext.Instance.ExperimentEnsembleResult)
            ?? throw new JsonException("The experiment result was JSON null.");

        Validate(result);
        return result;
    }

    private static void Validate(ExperimentEnsembleResult result)
    {
        if (result.Manifest is null || result.Metadata is null || result.Random is null ||
            result.ReplicateSeeds is null || result.Replicates is null || result.Summary is null ||
            result.Summary.FinalMetricStatistics is null)
        {
            throw new JsonException("Experiment result metadata and arrays are required.");
        }

        result.Manifest.Validate();
        result.Random.Validate();
        if (string.IsNullOrWhiteSpace(result.Metadata.SoftwareVersion) ||
            string.IsNullOrWhiteSpace(result.Metadata.RuntimeVersion) ||
            string.IsNullOrWhiteSpace(result.Metadata.OperatingSystem) ||
            string.IsNullOrWhiteSpace(result.Metadata.Architecture) ||
            result.Metadata.KernelId != "canonical-serial-v1" ||
            result.Metadata.SourceTreeState is not ("clean" or "dirty" or "unknown"))
        {
            throw new JsonException("Experiment run provenance is incomplete or unsupported.");
        }

        if (!double.IsFinite(result.ElapsedMilliseconds) || result.ElapsedMilliseconds < 0 || result.AllocatedBytes < 0)
        {
            throw new JsonException("Experiment ensemble timing or allocation metadata is invalid.");
        }

        int replicateCount = result.Manifest.ReplicateCount;
        if (result.ReplicateSeeds.Length != replicateCount || result.Replicates.Length != replicateCount ||
            result.Summary.AttemptedReplicates != replicateCount)
        {
            throw new JsonException("Experiment result replicate counts do not match the resolved manifest.");
        }

        bool[] seenIndices = new bool[replicateCount];
        for (int index = 0; index < replicateCount; index++)
        {
            if (result.ReplicateSeeds[index] != ReplicateSeedDerivation.Derive(result.Manifest.BaseSeed, index))
            {
                throw new JsonException($"Replicate seed at index {index} does not match the manifest.");
            }
        }

        foreach (ExperimentReplicateResult? replicate in result.Replicates)
        {
            if (replicate is null || replicate.Samples is null || replicate.Snapshots is null ||
                string.IsNullOrWhiteSpace(replicate.ReplicateId) ||
                replicate.ExperimentId != result.Manifest.ExperimentId ||
                (uint)replicate.ReplicateIndex >= (uint)replicateCount || seenIndices[replicate.ReplicateIndex])
            {
                throw new JsonException("Replicate identities must be unique and cover the manifest indices.");
            }

            int index = replicate.ReplicateIndex;
            ulong expectedSeed = ReplicateSeedDerivation.Derive(result.Manifest.BaseSeed, index);
            if (replicate.ReplicateId != $"{result.Manifest.ExperimentId}-r{index + 1:D4}" ||
                replicate.Seed != expectedSeed || result.ReplicateSeeds[index] != expectedSeed ||
                replicate.InitialisationSeed != ExperimentSeedDerivation.DeriveInitialisation(expectedSeed) ||
                replicate.DynamicsSeed != ExperimentSeedDerivation.DeriveDynamics(expectedSeed))
            {
                throw new JsonException($"Replicate {index} identity or derived seeds do not match the manifest.");
            }

            ValidateReplicate(result.Manifest, replicate);
            seenIndices[index] = true;
        }

        ExperimentEnsembleSummary recomputed;
        try
        {
            recomputed = ExperimentEnsembleSummary.Create(result.Manifest, result.Replicates);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("Raw replicate data does not form a valid ensemble.", exception);
        }

        ValidateSummary(result.Summary, recomputed);
    }

    private static void ValidateReplicate(ExperimentManifest manifest, ExperimentReplicateResult replicate)
    {
        if (!Enum.IsDefined(replicate.Status) ||
            (replicate.Status == ReplicateRunStatus.Succeeded && (replicate.Failure is not null || replicate.FinalMeasurements is null)) ||
            (replicate.Status == ReplicateRunStatus.Failed && string.IsNullOrWhiteSpace(replicate.Failure)))
        {
            throw new JsonException($"Replicate {replicate.ReplicateIndex} status and final measurement are inconsistent.");
        }

        if (replicate.AttemptCount < 0 || replicate.AcceptedAttemptCount < 0 || replicate.RejectedAttemptCount < 0 ||
            replicate.NoOpAttemptCount < 0 || replicate.ConnectivityFallbackCount < 0 || replicate.AllocatedBytes < 0 ||
            !double.IsFinite(replicate.ElapsedMilliseconds) || replicate.ElapsedMilliseconds < 0 ||
            !double.IsFinite(replicate.MeasurementMilliseconds) || replicate.MeasurementMilliseconds < 0 ||
            replicate.AcceptedAttemptCount + replicate.RejectedAttemptCount + replicate.NoOpAttemptCount > replicate.AttemptCount)
        {
            throw new JsonException($"Replicate {replicate.ReplicateIndex} counters or timings are invalid.");
        }

        long previousMcs = -1;
        foreach (MeasurementSample? sample in replicate.Samples)
        {
            if (sample is null || sample.Metrics is null || sample.Mcs < 0 || sample.Mcs > manifest.McsCount || sample.Mcs <= previousMcs)
            {
                throw new JsonException($"Replicate {replicate.ReplicateIndex} measurement samples are not ordered valid MCS values.");
            }

            ValidateMeasurements(sample.Metrics, manifest.CellTypes.Length);
            previousMcs = sample.Mcs;
        }

        if (replicate.Status == ReplicateRunStatus.Succeeded)
        {
            bool requiresZero = manifest.Measurements.IncludeMcsZero || manifest.McsCount == 0;
            if (replicate.Samples.Length == 0 || (requiresZero && replicate.Samples[0].Mcs != 0) ||
                replicate.Samples[^1].Mcs != manifest.McsCount || replicate.FinalMeasurements is null ||
                !MeasurementsEqual(replicate.FinalMeasurements, replicate.Samples[^1].Metrics) ||
                replicate.AcceptedAttemptCount + replicate.RejectedAttemptCount + replicate.NoOpAttemptCount != replicate.AttemptCount)
            {
                throw new JsonException($"Replicate {replicate.ReplicateIndex} final measurement or attempt totals are inconsistent.");
            }
        }

        long previousSnapshotMcs = -1;
        foreach (ExperimentSnapshot? snapshot in replicate.Snapshots)
        {
            if (snapshot is null)
            {
                throw new JsonException($"Replicate {replicate.ReplicateIndex} contains a null snapshot.");
            }

            try
            {
                snapshot.Validate();
            }
            catch (ArgumentException exception)
            {
                throw new JsonException($"Replicate {replicate.ReplicateIndex} contains an invalid snapshot.", exception);
            }

            if (snapshot.ExperimentId != manifest.ExperimentId || snapshot.ReplicateId != replicate.ReplicateId ||
                snapshot.ReplicateIndex != replicate.ReplicateIndex || snapshot.Seed != replicate.Seed ||
                snapshot.Mcs > manifest.McsCount || snapshot.Mcs <= previousSnapshotMcs)
            {
                throw new JsonException($"Replicate {replicate.ReplicateIndex} snapshot identity or schedule is invalid.");
            }

            previousSnapshotMcs = snapshot.Mcs;
        }
    }

    private static void ValidateMeasurements(TissueMeasurements measurements, int cellTypeCount)
    {
        if (measurements.HomotypicInterfacesByType is null || measurements.DomainsByType is null ||
            measurements.HomotypicInterfacesByType.Length != cellTypeCount - 1 ||
            measurements.DomainsByType.Length != cellTypeCount - 1 || measurements.HeterotypicInterfaceCount < 0 ||
            measurements.TotalCellCellInterfaceCount < 0 || measurements.HeterotypicInterfaceCount > measurements.TotalCellCellInterfaceCount ||
            measurements.TypeACellCount < 0 || measurements.TypeBCellCount < 0 ||
            !double.IsFinite(measurements.HeterotypicInterfaceFraction) ||
            !double.IsFinite(measurements.MeanCellArea) || !double.IsFinite(measurements.MeanCellPerimeter) ||
            measurements.MinimumCellArea < 0 || measurements.MaximumCellArea < 0 ||
            measurements.MinimumCellPerimeter < 0 || measurements.MaximumCellPerimeter < 0)
        {
            throw new JsonException("A tissue measurement record contains invalid or missing metrics.");
        }

        double expectedFraction = measurements.TotalCellCellInterfaceCount == 0
            ? 0
            : (double)measurements.HeterotypicInterfaceCount / measurements.TotalCellCellInterfaceCount;
        long homotypicTotal = 0;
        for (int index = 0; index < measurements.HomotypicInterfacesByType.Length; index++)
        {
            TypeInterfaceCount? homotypic = measurements.HomotypicInterfacesByType[index];
            TypeDomainCount? domains = measurements.DomainsByType[index];
            if (homotypic is null || domains is null || homotypic.TypeId != index + 1 || homotypic.Count < 0 ||
                domains.TypeId != index + 1 || domains.DomainCount < 0 || domains.LargestDomainCellCount < 0)
            {
                throw new JsonException("A tissue measurement has invalid type-specific contact or domain data.");
            }

            homotypicTotal = checked(homotypicTotal + homotypic.Count);
        }

        if (measurements.HeterotypicInterfaceFraction != expectedFraction ||
            homotypicTotal + measurements.HeterotypicInterfaceCount != measurements.TotalCellCellInterfaceCount)
        {
            throw new JsonException("A tissue measurement's interface counts and fraction are inconsistent.");
        }
    }

    private static bool MeasurementsEqual(TissueMeasurements first, TissueMeasurements second) =>
        first.HeterotypicInterfaceCount == second.HeterotypicInterfaceCount &&
        first.TotalCellCellInterfaceCount == second.TotalCellCellInterfaceCount &&
        first.HeterotypicInterfaceFraction == second.HeterotypicInterfaceFraction &&
        first.HomotypicInterfacesByType.SequenceEqual(second.HomotypicInterfacesByType) &&
        first.TypeACellCount == second.TypeACellCount &&
        first.TypeBCellCount == second.TypeBCellCount &&
        first.MeanCellArea == second.MeanCellArea &&
        first.MinimumCellArea == second.MinimumCellArea &&
        first.MaximumCellArea == second.MaximumCellArea &&
        first.MeanCellPerimeter == second.MeanCellPerimeter &&
        first.MinimumCellPerimeter == second.MinimumCellPerimeter &&
        first.MaximumCellPerimeter == second.MaximumCellPerimeter &&
        first.DomainsByType.SequenceEqual(second.DomainsByType);

    private static void ValidateSummary(ExperimentEnsembleSummary stored, ExperimentEnsembleSummary recomputed)
    {
        if (stored.AttemptedReplicates != recomputed.AttemptedReplicates ||
            stored.SuccessfulReplicates != recomputed.SuccessfulReplicates ||
            stored.FailedReplicates != recomputed.FailedReplicates || stored.FailurePolicy != recomputed.FailurePolicy ||
            stored.FinalMetricStatistics.Count != recomputed.FinalMetricStatistics.Count ||
            stored.FinalMetricStatistics.Keys.Except(recomputed.FinalMetricStatistics.Keys, StringComparer.Ordinal).Any())
        {
            throw new JsonException("Stored ensemble summary counts or metric IDs do not match the raw replicate results.");
        }

        foreach ((string metricId, MetricStatistics expected) in recomputed.FinalMetricStatistics)
        {
            if (!stored.FinalMetricStatistics.TryGetValue(metricId, out MetricStatistics? actual) ||
                actual is null || actual != expected)
            {
                throw new JsonException($"Stored statistics for metric '{metricId}' do not match the raw replicate results.");
            }
        }
    }
}
