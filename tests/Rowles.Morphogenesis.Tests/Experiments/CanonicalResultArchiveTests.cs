using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Initialisation;

namespace Rowles.Morphogenesis.Tests.Experiments;

public sealed class CanonicalResultArchiveTests
{
    [Fact]
    public void Archived_canonical_results_keep_raw_replicates_seeds_and_resolved_provenance()
    {
        string resultsDirectory = Path.Combine(AppContext.BaseDirectory, "experiments", "results");
        string[] paths = Directory.GetFiles(resultsDirectory, "*.json").Order(StringComparer.Ordinal).ToArray();
        HashSet<string> sourceCommits = new(StringComparer.Ordinal);

        Assert.Equal(8, paths.Length);
        foreach (string path in paths)
        {
            ExperimentEnsembleResult result = ExperimentEnsembleResult.FromJson(File.ReadAllText(path));

            Assert.Equal(ExperimentManifest.CurrentSchemaVersion, result.Manifest.SchemaVersion);
            Assert.Equal(result.Manifest.ReplicateCount, result.Summary.AttemptedReplicates);
            Assert.Equal(result.Manifest.ReplicateCount, result.ReplicateSeeds.Length);
            Assert.Equal(result.ReplicateSeeds.Length, result.ReplicateSeeds.Distinct().Count());
            Assert.Equal(result.Manifest.ReplicateCount, result.Replicates.Select(replicate => replicate.ReplicateIndex).Distinct().Count());
            Assert.Equal(Enumerable.Range(0, result.Manifest.ReplicateCount), result.Replicates.Select(replicate => replicate.ReplicateIndex).Order());
            Assert.Equal(result.Manifest.ReplicateCount, result.Replicates.Select(replicate => replicate.ReplicateId).Distinct().Count());
            Assert.Equal(ExperimentRandomMetadata.Current, result.Random);
            Assert.Equal(0, result.Summary.FailedReplicates);
            Assert.Equal("clean", result.Metadata.SourceTreeState);
            Assert.False(string.IsNullOrWhiteSpace(result.Metadata.SoftwareCommit));
            sourceCommits.Add(result.Metadata.SoftwareCommit!);
            Assert.All(result.Replicates, replicate =>
            {
                Assert.Equal(result.Manifest.ExperimentId, replicate.ExperimentId);
                Assert.Equal($"{result.Manifest.ExperimentId}-r{replicate.ReplicateIndex + 1:D4}", replicate.ReplicateId);
                Assert.Equal(ReplicateSeedDerivation.Derive(result.Manifest.BaseSeed, replicate.ReplicateIndex), replicate.Seed);
                Assert.Equal(replicate.Seed, result.ReplicateSeeds[replicate.ReplicateIndex]);
                Assert.Equal(ExperimentSeedDerivation.DeriveInitialisation(replicate.Seed), replicate.InitialisationSeed);
                Assert.Equal(ExperimentSeedDerivation.DeriveDynamics(replicate.Seed), replicate.DynamicsSeed);
                Assert.Equal(ReplicateRunStatus.Succeeded, replicate.Status);
                AssertMeasurementSchedule(result.Manifest, replicate.Samples.Select(sample => sample.Mcs));
                Assert.Equal(result.Manifest.McsCount, replicate.Samples[^1].Mcs);
                AssertSnapshotSchedule(result.Manifest, replicate.Snapshots.Select(snapshot => snapshot.Mcs));
                Assert.All(replicate.Snapshots, snapshot => snapshot.Validate());
            });
        }

        string resultRecordPath = Path.Combine(AppContext.BaseDirectory, "experiments", "results", "README.md");
        string expectedSourceLine = File.ReadLines(resultRecordPath)
            .Single(line => line.StartsWith("- Source commit: ", StringComparison.Ordinal));
        string expectedSourceCommit = expectedSourceLine.Split('`')[1];
        Assert.Equal(expectedSourceCommit, Assert.Single(sourceCommits));
    }

    [Fact]
    public void E02_control_and_sorting_results_share_the_same_initial_tissues_and_pass_the_archive_gate()
    {
        ExperimentEnsembleResult control = Read("E02-control-v1.json");
        ExperimentEnsembleResult sorting = Read("E02-sorting-v1.json");
        ExperimentEnsembleResult perturbedControl = Read("E02-control-perturbation-v1.json");
        ExperimentEnsembleResult perturbedSorting = Read("E02-sorting-perturbation-v1.json");

        AssertSameInitialTissues(control, sorting);
        AssertSameInitialTissues(perturbedControl, perturbedSorting);
        Assert.Equal(16, control.Summary.SuccessfulReplicates);
        Assert.Equal(16, sorting.Summary.SuccessfulReplicates);
        Assert.Equal(16, perturbedControl.Summary.SuccessfulReplicates);
        Assert.Equal(16, perturbedSorting.Summary.SuccessfulReplicates);
        AssertSortingConclusion(control, sorting);
        AssertSortingConclusion(perturbedControl, perturbedSorting);
    }

    private static ExperimentEnsembleResult Read(string name) => ExperimentEnsembleResult.FromJson(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "experiments", "results", name)));

    private static void AssertSameInitialTissues(ExperimentEnsembleResult first, ExperimentEnsembleResult second)
    {
        Assert.Equal(first.ReplicateSeeds, second.ReplicateSeeds);
        for (int index = 0; index < first.Replicates.Length; index++)
        {
            var firstMetrics = first.Replicates[index].Samples[0].Metrics;
            var secondMetrics = second.Replicates[index].Samples[0].Metrics;
            Assert.Equal(firstMetrics.HeterotypicInterfaceCount, secondMetrics.HeterotypicInterfaceCount);
            Assert.Equal(firstMetrics.TotalCellCellInterfaceCount, secondMetrics.TotalCellCellInterfaceCount);
            Assert.Equal(firstMetrics.HeterotypicInterfaceFraction, secondMetrics.HeterotypicInterfaceFraction);
            Assert.Equal(firstMetrics.TypeACellCount, secondMetrics.TypeACellCount);
            Assert.Equal(firstMetrics.TypeBCellCount, secondMetrics.TypeBCellCount);

            var firstInitial = PackedAggregateInitialiser.Create(first.Manifest, first.Replicates[index].InitialisationSeed).State;
            var secondInitial = PackedAggregateInitialiser.Create(second.Manifest, second.Replicates[index].InitialisationSeed).State;
            Assert.Equal(firstInitial.GetCellIdsCopy(), secondInitial.GetCellIdsCopy());
            for (int cellId = 1; cellId <= first.Manifest.Initialiser.CellCount; cellId++)
            {
                Assert.Equal(firstInitial.GetCellState(cellId).CellTypeId, secondInitial.GetCellState(cellId).CellTypeId);
            }
        }
    }

    private static void AssertMeasurementSchedule(ExperimentManifest manifest, IEnumerable<long> actualMcs)
    {
        long[] actual = actualMcs.ToArray();
        List<long> expected = [];
        if (manifest.Measurements.IncludeMcsZero || manifest.McsCount == 0)
        {
            expected.Add(0);
        }

        for (long mcs = manifest.Measurements.EveryMcs; mcs <= manifest.McsCount; mcs += manifest.Measurements.EveryMcs)
        {
            expected.Add(mcs);
        }

        if (expected.Count == 0 || expected[^1] != manifest.McsCount)
        {
            expected.Add(manifest.McsCount);
        }

        Assert.Equal(expected, actual);
    }

    private static void AssertSnapshotSchedule(ExperimentManifest manifest, IEnumerable<long> actualMcs)
    {
        long[] actual = actualMcs.ToArray();
        if (manifest.Measurements.SnapshotEveryMcs == 0)
        {
            Assert.Empty(actual);
            return;
        }

        List<long> expected = [0];
        for (long mcs = manifest.Measurements.SnapshotEveryMcs; mcs <= manifest.McsCount; mcs += manifest.Measurements.SnapshotEveryMcs)
        {
            expected.Add(mcs);
        }

        if (expected[^1] != manifest.McsCount)
        {
            expected.Add(manifest.McsCount);
        }

        Assert.Equal(expected, actual);
    }

    private static void AssertSortingConclusion(ExperimentEnsembleResult control, ExperimentEnsembleResult sorting)
    {
        double controlMedian = control.Summary.FinalMetricStatistics["heterotypic-interface-fraction"].Median;
        double sortingMedian = sorting.Summary.FinalMetricStatistics["heterotypic-interface-fraction"].Median;
        Assert.True(controlMedian - sortingMedian >= 0.15);
        Assert.True(
            sorting.Replicates.Max(result => result.FinalMeasurements!.HeterotypicInterfaceFraction) <
            control.Replicates.Min(result => result.FinalMeasurements!.HeterotypicInterfaceFraction));

        double initialHomotypicMedian = Median(sorting.Replicates.Select(replicate =>
            (double)replicate.Samples[0].Metrics.HomotypicInterfacesByType.Sum(count => count.Count)));
        double sortingHomotypicMedian = Median(sorting.Replicates.Select(replicate =>
            (double)replicate.FinalMeasurements!.HomotypicInterfacesByType.Sum(count => count.Count)));
        double controlHomotypicMedian = Median(control.Replicates.Select(replicate =>
            (double)replicate.FinalMeasurements!.HomotypicInterfacesByType.Sum(count => count.Count)));
        Assert.True(sortingHomotypicMedian > initialHomotypicMedian);
        Assert.True(sortingHomotypicMedian > controlHomotypicMedian);
        Assert.All(sorting.Replicates, replicate =>
            Assert.True(replicate.FinalMeasurements!.TotalCellCellInterfaceCount >
                replicate.Samples[0].Metrics.TotalCellCellInterfaceCount * 0.25));
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] ordered = values.Order().ToArray();
        return ordered.Length % 2 == 0
            ? (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2
            : ordered[ordered.Length / 2];
    }
}
