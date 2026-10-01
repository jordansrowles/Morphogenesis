using System.Text.Json;
using System.Text.Json.Nodes;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Initialisation;
using Rowles.Morphogenesis.Measurements;
using Rowles.Morphogenesis.Random;

namespace Rowles.Morphogenesis.Tests.Experiments;

public sealed class ExperimentRunnerTests
{
    [Fact]
    public void Replicate_seed_derivation_is_stable_and_unique_for_bounded_ranges()
    {
        ulong[] first = ReplicateSeedDerivation.DeriveRange(123456789, 1024);
        ulong[] second = ReplicateSeedDerivation.DeriveRange(123456789, 1024);

        Assert.Equal(first, second);
        Assert.Equal(1024, first.Distinct().Count());
        Assert.Equal(first[417], ReplicateSeedDerivation.Derive(123456789, 417));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReplicateSeedDerivation.Derive(0, -1));
    }

    [Fact]
    public void Experiment_seed_domains_are_stable_distinct_and_order_independent()
    {
        ulong[] replicateSeeds = ReplicateSeedDerivation.DeriveRange(20261001, 4096);
        HashSet<ulong> initialisationSeeds = [];
        HashSet<ulong> dynamicsSeeds = [];

        foreach (ulong replicateSeed in replicateSeeds)
        {
            ulong firstInitialisation = ExperimentSeedDerivation.DeriveInitialisation(replicateSeed);
            ulong secondInitialisation = ExperimentSeedDerivation.DeriveInitialisation(replicateSeed);
            ulong firstDynamics = ExperimentSeedDerivation.DeriveDynamics(replicateSeed);
            ulong secondDynamics = ExperimentSeedDerivation.DeriveDynamics(replicateSeed);

            Assert.Equal(firstInitialisation, secondInitialisation);
            Assert.Equal(firstDynamics, secondDynamics);
            Assert.NotEqual(firstInitialisation, firstDynamics);
            initialisationSeeds.Add(firstInitialisation);
            dynamicsSeeds.Add(firstDynamics);
        }

        Assert.Equal(replicateSeeds.Length, initialisationSeeds.Count);
        Assert.Equal(replicateSeeds.Length, dynamicsSeeds.Count);
        ulong[] reversedInitialisation = replicateSeeds.Reverse()
            .Select(ExperimentSeedDerivation.DeriveInitialisation)
            .Reverse()
            .ToArray();
        ulong[] reversedDynamics = replicateSeeds.Reverse()
            .Select(ExperimentSeedDerivation.DeriveDynamics)
            .Reverse()
            .ToArray();
        Assert.Equal(replicateSeeds.Select(ExperimentSeedDerivation.DeriveInitialisation), reversedInitialisation);
        Assert.Equal(replicateSeeds.Select(ExperimentSeedDerivation.DeriveDynamics), reversedDynamics);

        Assert.Equal(Xoshiro256StarStar.AlgorithmName, ExperimentRandomMetadata.Current.Algorithm);
        Assert.Equal("splitmix64-domain-v1", ExperimentRandomMetadata.Current.SeedDerivationScheme);
    }

    [Fact]
    public void Matched_contact_conditions_use_identical_initial_occupancy_and_type_assignment()
    {
        ExperimentManifest control = ExperimentManifestFactory.Create();
        ExperimentManifest sorting = control with
        {
            ContactEnergies =
            [
                [0, 8, 8],
                [8, 4, 20],
                [8, 20, 4]
            ]
        };
        ulong replicateSeed = ReplicateSeedDerivation.Derive(control.BaseSeed, 0);
        ulong initialisationSeed = ExperimentSeedDerivation.DeriveInitialisation(replicateSeed);

        PackedAggregateInitialisation controlState = PackedAggregateInitialiser.Create(control, initialisationSeed);
        PackedAggregateInitialisation sortingState = PackedAggregateInitialiser.Create(sorting, initialisationSeed);

        Assert.Equal(controlState.State.GetCellIdsCopy(), sortingState.State.GetCellIdsCopy());
        for (int cellId = 1; cellId <= control.Initialiser.CellCount; cellId++)
        {
            Assert.Equal(controlState.State.GetCellState(cellId).CellTypeId, sortingState.State.GetCellState(cellId).CellTypeId);
        }
    }

    [Fact]
    public void Measurement_schedule_includes_zero_cadence_and_final_state()
    {
        ExperimentManifest original = ExperimentManifestFactory.Create();
        ExperimentManifest manifest = original with
        {
            McsCount = 5,
            ReplicateCount = 1,
            Measurements = original.Measurements with
            {
                EveryMcs = 2,
                IncludeMcsZero = true,
                SnapshotEveryMcs = 3
            }
        };

        ExperimentReplicateResult result = ExperimentRunner.RunReplicate(manifest, 0);

        Assert.Equal(ReplicateRunStatus.Succeeded, result.Status);
        Assert.Equal([0L, 2L, 4L, 5L], result.Samples.Select(sample => sample.Mcs));
        Assert.Equal([0L, 3L, 5L], result.Snapshots.Select(snapshot => snapshot.Mcs));
        Assert.Equal(16 * 16 * 5, result.AttemptCount);
        Assert.Equal(result.AttemptCount,
            result.AcceptedAttemptCount + result.RejectedAttemptCount + result.NoOpAttemptCount);
        Assert.True(result.FinalMeasurements is not null);
    }

    [Fact]
    public void MCS_zero_can_be_disabled_without_suppressing_the_final_measurement()
    {
        ExperimentManifest original = ExperimentManifestFactory.Create();
        ExperimentManifest manifest = original with
        {
            McsCount = 5,
            ReplicateCount = 1,
            Measurements = original.Measurements with { EveryMcs = 2, IncludeMcsZero = false }
        };

        ExperimentReplicateResult result = ExperimentRunner.RunReplicate(manifest, 0);

        Assert.Equal([2L, 4L, 5L], result.Samples.Select(sample => sample.Mcs));
        Assert.Equal(5, result.Samples[^1].Mcs);
    }

    [Fact]
    public void Reordering_serial_replicate_execution_preserves_per_replicate_results()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create() with { McsCount = 3 };

        ExperimentEnsembleResult forward = ExperimentRunner.Run(manifest);
        ExperimentEnsembleResult reversed = ExperimentRunner.Run(manifest, [2, 1, 0]);

        Assert.Equal(forward.ReplicateSeeds, reversed.ReplicateSeeds);
        for (int index = 0; index < manifest.ReplicateCount; index++)
        {
            ExperimentReplicateResult first = forward.Replicates[index];
            ExperimentReplicateResult second = reversed.Replicates[index];
            Assert.Equal(first.ReplicateId, second.ReplicateId);
            Assert.Equal(first.Seed, second.Seed);
            Assert.Equal(first.InitialisationSeed, second.InitialisationSeed);
            Assert.Equal(first.DynamicsSeed, second.DynamicsSeed);
            Assert.Equal(first.Status, second.Status);
            Assert.Equal(JsonSerializer.Serialize(first.Samples), JsonSerializer.Serialize(second.Samples));
            Assert.Equal(first.AttemptCount, second.AttemptCount);
        }
    }

    [Fact]
    public void Result_json_preserves_manifest_replicate_seeds_and_raw_series()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create() with { McsCount = 2 };
        ExperimentEnsembleResult result = ExperimentRunner.Run(manifest);

        ExperimentEnsembleResult actual = ExperimentEnsembleResult.FromJson(result.ToJson());

        Assert.Equal(manifest.ToJson(), actual.Manifest.ToJson());
        Assert.Equal(result.ReplicateSeeds, actual.ReplicateSeeds);
        Assert.Equal(ExperimentRandomMetadata.Current, actual.Random);
        Assert.Equal(result.Replicates.Select(replicate => replicate.InitialisationSeed),
            actual.Replicates.Select(replicate => replicate.InitialisationSeed));
        Assert.Equal(result.Replicates.Select(replicate => replicate.DynamicsSeed),
            actual.Replicates.Select(replicate => replicate.DynamicsSeed));
        Assert.Equal(result.Summary.AttemptedReplicates, actual.Summary.AttemptedReplicates);
        Assert.Equal(JsonSerializer.Serialize(result.Replicates.Select(replicate => replicate.Samples)),
            JsonSerializer.Serialize(actual.Replicates.Select(replicate => replicate.Samples)));
    }

    [Fact]
    public void Aggregate_statistics_use_successes_and_keep_failed_replicates_visible()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create() with { ReplicateCount = 2 };
        TissueMeasurements metrics = new(
            2,
            4,
            0.5,
            [new TypeInterfaceCount(1, 1), new TypeInterfaceCount(2, 1)],
            2,
            2,
            4,
            3,
            5,
            20,
            18,
            22,
            [new TypeDomainCount(1, 1, 2), new TypeDomainCount(2, 1, 2)]);
        ulong firstSeed = ReplicateSeedDerivation.Derive(manifest.BaseSeed, 0);
        ulong secondSeed = ReplicateSeedDerivation.Derive(manifest.BaseSeed, 1);
        ExperimentReplicateResult succeeded = new(
            "test-sorting", "test-sorting-r0001", 0, firstSeed,
            ExperimentSeedDerivation.DeriveInitialisation(firstSeed), ExperimentSeedDerivation.DeriveDynamics(firstSeed), ReplicateRunStatus.Succeeded,
            null, [], metrics, [], 100, 60, 30, 10, 2, 5, 1000, 1);
        ExperimentReplicateResult failed = new(
            "test-sorting", "test-sorting-r0002", 1, secondSeed,
            ExperimentSeedDerivation.DeriveInitialisation(secondSeed), ExperimentSeedDerivation.DeriveDynamics(secondSeed), ReplicateRunStatus.Failed,
            "InvalidOperationException: diagnostic failure", [], null, [], 40, 20, 15, 5, 1, 2, 500, 0.5);

        ExperimentEnsembleSummary summary = ExperimentRunner.SummariseResults(manifest, [succeeded, failed]);

        Assert.Equal(2, summary.AttemptedReplicates);
        Assert.Equal(1, summary.SuccessfulReplicates);
        Assert.Equal(1, summary.FailedReplicates);
        Assert.Equal(1, summary.FinalMetricStatistics["heterotypic-interface-fraction"].Count);
        Assert.Equal(0.5, summary.FinalMetricStatistics["heterotypic-interface-fraction"].Mean);
        Assert.Equal(0.6, summary.FinalMetricStatistics["accepted-attempt-fraction"].Mean);
        Assert.Equal(1, summary.FinalMetricStatistics["total-homotypic-interface-count"].Count);
        Assert.Contains("failed counts remain explicit", summary.FailurePolicy);
    }

    [Fact]
    public void Metric_statistics_report_sample_standard_deviation_and_empty_sets()
    {
        MetricStatistics values = MetricStatistics.From([1, 3, 5]);
        MetricStatistics empty = MetricStatistics.From([]);

        Assert.Equal(3, values.Count);
        Assert.Equal(3, values.Mean);
        Assert.Equal(3, values.Median);
        Assert.Equal(2, values.SampleStandardDeviation);
        Assert.Equal(1, values.Minimum);
        Assert.Equal(5, values.Maximum);
        Assert.Equal(0, empty.Count);
        Assert.Equal(0, empty.Mean);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("mean")]
    [InlineData("median")]
    [InlineData("sampleStandardDeviation")]
    [InlineData("minimum")]
    [InlineData("maximum")]
    public void Result_json_rejects_tampered_stored_metric_statistics(string field)
    {
        ExperimentEnsembleResult result = ExperimentRunner.Run(ExperimentManifestFactory.Create() with { McsCount = 2 });
        JsonObject json = JsonNode.Parse(result.ToJson())!.AsObject();
        JsonObject metric = json["summary"]!["finalMetricStatistics"]!["heterotypic-interface-fraction"]!.AsObject();
        if (field == "count")
        {
            metric[field] = metric[field]!.GetValue<int>() + 1;
        }
        else
        {
            metric[field] = metric[field]!.GetValue<double>() + 0.01;
        }

        Assert.Throws<JsonException>(() => ExperimentEnsembleResult.FromJson(json.ToJsonString()));
    }

    [Theory]
    [InlineData("seed")]
    [InlineData("initialisationSeed")]
    [InlineData("dynamicsSeed")]
    public void Result_json_rejects_tampered_replicate_seed_mapping(string field)
    {
        ExperimentEnsembleResult result = ExperimentRunner.Run(ExperimentManifestFactory.Create() with { McsCount = 2 });
        JsonObject json = JsonNode.Parse(result.ToJson())!.AsObject();
        JsonObject replicate = json["replicates"]!.AsArray()[0]!.AsObject();
        replicate[field] = replicate[field]!.GetValue<ulong>() ^ 1UL;

        Assert.Throws<JsonException>(() => ExperimentEnsembleResult.FromJson(json.ToJsonString()));
    }

    [Fact]
    public void Result_json_rejects_unsupported_prng_metadata()
    {
        ExperimentEnsembleResult result = ExperimentRunner.Run(ExperimentManifestFactory.Create() with { McsCount = 2 });
        JsonObject json = JsonNode.Parse(result.ToJson())!.AsObject();
        json["random"]!["algorithm"] = "other-generator";

        Assert.Throws<NotSupportedException>(() => ExperimentEnsembleResult.FromJson(json.ToJsonString()));
    }
}
