using System.Diagnostics;
using System.Runtime.InteropServices;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Random;
using Rowles.Morphogenesis.Experiments.Results;
using Rowles.Morphogenesis.Initialisation;
using Rowles.Morphogenesis.Measurements;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Random;
using Rowles.Morphogenesis.Snapshots;

namespace Rowles.Morphogenesis.Experiments.Execution;

/// <summary>Runs independent replicates sequentially through the canonical serial kernel.</summary>
public static class ExperimentRunner
{
    public static ExperimentEnsembleResult Run(
        ExperimentManifest manifest,
        IReadOnlyList<int>? executionOrder = null,
        string? softwareCommit = null,
        Action<int, int>? progress = null,
        string? sdkVersion = null,
        string sourceTreeState = "unknown")
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();
        int[] order = executionOrder?.ToArray() ?? Enumerable.Range(0, manifest.ReplicateCount).ToArray();
        ValidateExecutionOrder(order, manifest.ReplicateCount);

        ulong[] seeds = ReplicateSeedDerivation.DeriveRange(manifest.BaseSeed, manifest.ReplicateCount);
        ExperimentReplicateResult[] byIndex = new ExperimentReplicateResult[manifest.ReplicateCount];
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int completed = 0; completed < order.Length; completed++)
        {
            int replicateIndex = order[completed];
            byIndex[replicateIndex] = RunReplicate(manifest, replicateIndex, seeds[replicateIndex]);
            progress?.Invoke(completed + 1, order.Length);
        }

        stopwatch.Stop();
        long allocatedBytes = Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore);
        ExperimentReplicateResult[] replicates = byIndex;
        ExperimentEnsembleSummary summary = SummariseResults(manifest, replicates);
        ExperimentRunMetadata metadata = new(
            typeof(SerialSimulation).Assembly.GetName().Version?.ToString() ?? "unknown",
            softwareCommit,
            Environment.Version.ToString(),
            sdkVersion,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            "canonical-serial-v1",
            sourceTreeState);
        return new ExperimentEnsembleResult(
            manifest,
            metadata,
            ExperimentRandomMetadata.Current,
            seeds,
            replicates,
            summary,
            stopwatch.Elapsed.TotalMilliseconds,
            allocatedBytes);
    }

    public static ExperimentReplicateResult RunReplicate(ExperimentManifest manifest, int replicateIndex)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();
        if ((uint)replicateIndex >= (uint)manifest.ReplicateCount)
        {
            throw new ArgumentOutOfRangeException(nameof(replicateIndex));
        }

        return RunReplicate(manifest, replicateIndex, ReplicateSeedDerivation.Derive(manifest.BaseSeed, replicateIndex));
    }

    private static ExperimentReplicateResult RunReplicate(ExperimentManifest manifest, int replicateIndex, ulong seed)
    {
        string replicateId = $"{manifest.ExperimentId}-r{replicateIndex + 1:D4}";
        ulong initialisationSeed = ExperimentSeedDerivation.DeriveInitialisation(seed);
        ulong dynamicsSeed = ExperimentSeedDerivation.DeriveDynamics(seed);
        List<MeasurementSample> samples = [];
        List<ExperimentSnapshot> snapshots = [];
        Stopwatch stopwatch = Stopwatch.StartNew();
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        long measurementTicks = 0;
        long attemptCount = 0;
        long acceptedAttemptCount = 0;
        long rejectedAttemptCount = 0;
        long noOpAttemptCount = 0;
        long connectivityFallbackCount = 0;
        TissueMeasurements? finalMeasurements = null;
        SerialSimulation? simulation = null;
        try
        {
            PackedAggregateInitialisation initialisation = PackedAggregateInitialiser.Create(manifest, initialisationSeed);
            simulation = new(initialisation.State, new Xoshiro256StarStar(dynamicsSeed), manifest.FluctuationAmplitude);
            MorphogenesisState state = simulation.State;

            if (manifest.Measurements.IncludeMcsZero || manifest.McsCount == 0)
            {
                MeasureAndRecord(0);
            }

            if (manifest.Measurements.SnapshotEveryMcs > 0)
            {
                snapshots.Add(ExperimentSnapshot.Capture(manifest, replicateId, replicateIndex, seed, 0, state));
            }

            for (long mcs = 1; mcs <= manifest.McsCount; mcs++)
            {
                McsSummary mcsSummary = simulation.RunMcs();
                acceptedAttemptCount += mcsSummary.Accepted;
                rejectedAttemptCount += mcsSummary.Rejected;
                noOpAttemptCount += mcsSummary.NoOps;
                attemptCount = simulation.AttemptCount;
                connectivityFallbackCount = simulation.ConnectivityFallbackCount;
                if (manifest.Measurements.ValidateInvariantsEveryMcs > 0 &&
                    (mcs % manifest.Measurements.ValidateInvariantsEveryMcs == 0 || mcs == manifest.McsCount))
                {
                    state.ValidateInvariants();
                }

                if (mcs % manifest.Measurements.EveryMcs == 0 || mcs == manifest.McsCount)
                {
                    MeasureAndRecord(mcs);
                }

                if (manifest.Measurements.SnapshotEveryMcs > 0 &&
                    (mcs % manifest.Measurements.SnapshotEveryMcs == 0 || mcs == manifest.McsCount))
                {
                    snapshots.Add(ExperimentSnapshot.Capture(manifest, replicateId, replicateIndex, seed, mcs, state));
                }
            }

            state.ValidateInvariants();
            attemptCount = simulation.AttemptCount;
            connectivityFallbackCount = simulation.ConnectivityFallbackCount;
            stopwatch.Stop();
            long allocatedBytes = Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore);
            return new ExperimentReplicateResult(
                manifest.ExperimentId,
                replicateId,
                replicateIndex,
                seed,
                initialisationSeed,
                dynamicsSeed,
                ReplicateRunStatus.Succeeded,
                null,
                samples.ToArray(),
                finalMeasurements,
                snapshots.ToArray(),
                attemptCount,
                acceptedAttemptCount,
                rejectedAttemptCount,
                noOpAttemptCount,
                connectivityFallbackCount,
                stopwatch.Elapsed.TotalMilliseconds,
                allocatedBytes,
                TimeSpan.FromSeconds(measurementTicks / (double)Stopwatch.Frequency).TotalMilliseconds);

            TissueMeasurements MeasureAndRecord(long mcs)
            {
                long started = Stopwatch.GetTimestamp();
                TissueMeasurements metrics = TissueMeasurementCalculator.Measure(
                    state,
                    manifest.Initialiser.TypeAId,
                    manifest.Initialiser.TypeBId,
                    manifest.Measurements.InterfaceNeighbourhood);
                measurementTicks += Stopwatch.GetTimestamp() - started;
                samples.Add(new MeasurementSample(mcs, metrics));
                if (mcs == manifest.McsCount)
                {
                    finalMeasurements = metrics;
                }

                return metrics;
            }
        }
        catch (Exception exception)
        {
            if (simulation is not null)
            {
                attemptCount = simulation.AttemptCount;
                connectivityFallbackCount = simulation.ConnectivityFallbackCount;
            }

            stopwatch.Stop();
            long allocatedBytes = Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore);
            string failure = $"{exception.GetType().Name}: {exception.Message}";
            return new ExperimentReplicateResult(
                manifest.ExperimentId,
                replicateId,
                replicateIndex,
                seed,
                initialisationSeed,
                dynamicsSeed,
                ReplicateRunStatus.Failed,
                failure,
                samples.ToArray(),
                finalMeasurements,
                snapshots.ToArray(),
                attemptCount,
                acceptedAttemptCount,
                rejectedAttemptCount,
                noOpAttemptCount,
                connectivityFallbackCount,
                stopwatch.Elapsed.TotalMilliseconds,
                allocatedBytes,
                TimeSpan.FromSeconds(measurementTicks / (double)Stopwatch.Frequency).TotalMilliseconds);
        }
    }

    public static ExperimentEnsembleSummary SummariseResults(
        ExperimentManifest manifest,
        IReadOnlyList<ExperimentReplicateResult> replicates)
        => ExperimentEnsembleSummary.Create(manifest, replicates);

    private static void ValidateExecutionOrder(int[] order, int replicateCount)
    {
        if (order.Length != replicateCount)
        {
            throw new ArgumentException("Execution order must include every replicate exactly once.", nameof(order));
        }

        bool[] seen = new bool[replicateCount];
        foreach (int index in order)
        {
            if ((uint)index >= (uint)replicateCount || seen[index])
            {
                throw new ArgumentException("Execution order must be a permutation of all replicate indices.", nameof(order));
            }

            seen[index] = true;
        }
    }
}
