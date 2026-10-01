using Rowles.Morphogenesis.Experiments;

namespace Rowles.Morphogenesis.Tests.Scientific;

public sealed class ClassicalSortingScientificTests
{
    [Fact]
    [Trait("Category", "Scientific")]
    public void E01_fluctuation_sweep_changes_the_accepted_attempt_distribution()
    {
        ExperimentEnsembleResult low = RunReduced(Load("E01-fluctuation-low.json"));
        ExperimentEnsembleResult middle = RunReduced(Load("E01-fluctuation-middle.json"));
        ExperimentEnsembleResult high = RunReduced(Load("E01-fluctuation-high.json"));

        AssertSuccessful(low);
        AssertSuccessful(middle);
        AssertSuccessful(high);
        Assert.Equal(low.ReplicateSeeds, middle.ReplicateSeeds);
        Assert.Equal(low.ReplicateSeeds, high.ReplicateSeeds);

        double lowAcceptance = low.Summary.FinalMetricStatistics["accepted-attempt-fraction"].Mean;
        double middleAcceptance = middle.Summary.FinalMetricStatistics["accepted-attempt-fraction"].Mean;
        double highAcceptance = high.Summary.FinalMetricStatistics["accepted-attempt-fraction"].Mean;

        Assert.True(highAcceptance > lowAcceptance,
            $"Expected high-amplitude acceptance {highAcceptance:F4} above low-amplitude acceptance {lowAcceptance:F4}.");
        Assert.True(middleAcceptance > lowAcceptance,
            $"Expected middle-amplitude acceptance {middleAcceptance:F4} above low-amplitude acceptance {lowAcceptance:F4}.");
        Assert.True(highAcceptance > middleAcceptance,
            $"Expected high-amplitude acceptance {highAcceptance:F4} above middle-amplitude acceptance {middleAcceptance:F4}.");
    }

    [Fact]
    [Trait("Category", "Scientific")]
    public void E02_sorting_regime_is_less_mixed_than_control_after_population_perturbation()
    {
        ExperimentManifest control = Load("E02-control.json");
        ExperimentManifest sorting = Load("E02-sorting.json");
        ExperimentManifest perturbedControl = Load("E02-control-perturbation.json");
        ExperimentManifest perturbedSorting = Load("E02-sorting-perturbation.json");

        ExperimentEnsembleResult controlResult = RunReduced(control);
        ExperimentEnsembleResult sortingResult = RunReduced(sorting);
        ExperimentEnsembleResult perturbedControlResult = RunReduced(perturbedControl);
        ExperimentEnsembleResult perturbedSortingResult = RunReduced(perturbedSorting);

        AssertSuccessful(controlResult);
        AssertSuccessful(sortingResult);
        AssertSuccessful(perturbedControlResult);
        AssertSuccessful(perturbedSortingResult);
        AssertEqualStartingMetrics(controlResult, sortingResult);
        AssertEqualStartingMetrics(perturbedControlResult, perturbedSortingResult);
        AssertAntiDispersalSupport(controlResult, sortingResult, "base condition");
        AssertAntiDispersalSupport(perturbedControlResult, perturbedSortingResult, "population perturbation");

        double controlMedian = controlResult.Summary.FinalMetricStatistics["heterotypic-interface-fraction"].Median;
        double sortingMedian = sortingResult.Summary.FinalMetricStatistics["heterotypic-interface-fraction"].Median;
        double perturbedControlMedian = perturbedControlResult.Summary.FinalMetricStatistics["heterotypic-interface-fraction"].Median;
        double perturbedSortingMedian = perturbedSortingResult.Summary.FinalMetricStatistics["heterotypic-interface-fraction"].Median;

        Assert.True(controlMedian - sortingMedian >= 0.15,
            $"Expected sorting median {sortingMedian:F4} below control median {controlMedian:F4}.");
        Assert.True(perturbedControlMedian - perturbedSortingMedian >= 0.15,
            $"Expected perturbed sorting median {perturbedSortingMedian:F4} below perturbed control median {perturbedControlMedian:F4}.");
        Assert.True(sortingResult.Replicates.Max(result => result.FinalMeasurements!.HeterotypicInterfaceFraction) <
            controlResult.Replicates.Min(result => result.FinalMeasurements!.HeterotypicInterfaceFraction),
            "The reduced-cost base-condition replicate distributions should remain separated.");
        Assert.True(perturbedSortingResult.Replicates.Max(result => result.FinalMeasurements!.HeterotypicInterfaceFraction) <
            perturbedControlResult.Replicates.Min(result => result.FinalMeasurements!.HeterotypicInterfaceFraction),
            "The reduced-cost perturbed replicate distributions should remain separated.");
    }

    private static ExperimentEnsembleResult RunReduced(ExperimentManifest manifest) =>
        ExperimentRunner.Run(manifest with
        {
            McsCount = 50,
            ReplicateCount = 8,
            Measurements = manifest.Measurements with { SnapshotEveryMcs = 0 }
        });

    private static ExperimentManifest Load(string name) => ExperimentManifest.ReadJson(
        Path.Combine(AppContext.BaseDirectory, "experiments", "canonical", name));

    private static void AssertSuccessful(ExperimentEnsembleResult result)
    {
        Assert.Equal(8, result.Summary.AttemptedReplicates);
        Assert.Equal(8, result.Summary.SuccessfulReplicates);
        Assert.Equal(0, result.Summary.FailedReplicates);
        Assert.All(result.Replicates, replicate => Assert.Equal(ReplicateRunStatus.Succeeded, replicate.Status));
    }

    private static void AssertEqualStartingMetrics(ExperimentEnsembleResult first, ExperimentEnsembleResult second)
    {
        Assert.Equal(first.ReplicateSeeds, second.ReplicateSeeds);
        for (int replicateIndex = 0; replicateIndex < first.Replicates.Length; replicateIndex++)
        {
            Assert.Equal(first.Replicates[replicateIndex].Samples[0].Metrics.HeterotypicInterfaceCount,
                second.Replicates[replicateIndex].Samples[0].Metrics.HeterotypicInterfaceCount);
            Assert.Equal(first.Replicates[replicateIndex].Samples[0].Metrics.TotalCellCellInterfaceCount,
                second.Replicates[replicateIndex].Samples[0].Metrics.TotalCellCellInterfaceCount);
            Assert.Equal(first.Replicates[replicateIndex].Samples[0].Metrics.HeterotypicInterfaceFraction,
                second.Replicates[replicateIndex].Samples[0].Metrics.HeterotypicInterfaceFraction);
        }
    }

    private static void AssertAntiDispersalSupport(
        ExperimentEnsembleResult control,
        ExperimentEnsembleResult sorting,
        string condition)
    {
        double sortingInitialHomotypicMedian = Median(sorting.Replicates.Select(replicate =>
            (double)replicate.Samples[0].Metrics.HomotypicInterfacesByType.Sum(count => count.Count)));
        double sortingFinalHomotypicMedian = Median(sorting.Replicates.Select(replicate =>
            (double)replicate.FinalMeasurements!.HomotypicInterfacesByType.Sum(count => count.Count)));
        double controlFinalHomotypicMedian = Median(control.Replicates.Select(replicate =>
            (double)replicate.FinalMeasurements!.HomotypicInterfacesByType.Sum(count => count.Count)));

        Assert.True(sortingFinalHomotypicMedian > sortingInitialHomotypicMedian,
            $"Expected sorting to increase total homotypic interfaces from {sortingInitialHomotypicMedian:F2} to {sortingFinalHomotypicMedian:F2} in the {condition}.");
        Assert.True(sortingFinalHomotypicMedian > controlFinalHomotypicMedian,
            $"Expected sorting homotypic interfaces {sortingFinalHomotypicMedian:F2} above control {controlFinalHomotypicMedian:F2} in the {condition}.");

        Assert.All(sorting.Replicates, replicate =>
        {
            long initialContacts = replicate.Samples[0].Metrics.TotalCellCellInterfaceCount;
            long finalContacts = replicate.FinalMeasurements!.TotalCellCellInterfaceCount;
            Assert.True(finalContacts > initialContacts * 0.25,
                $"Replicate {replicate.ReplicateId} retained {finalContacts} of {initialContacts} initial cell-cell interfaces in the {condition}.");
        });
    }

    private static double Median(IEnumerable<double> source)
    {
        double[] ordered = source.Order().ToArray();
        return ordered.Length % 2 == 0
            ? (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2
            : ordered[ordered.Length / 2];
    }
}
