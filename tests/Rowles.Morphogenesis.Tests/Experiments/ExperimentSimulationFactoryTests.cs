using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Execution;
using Rowles.Morphogenesis.Experiments.Random;
using Rowles.Morphogenesis.Initialisation;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Random;

namespace Rowles.Morphogenesis.Tests.Experiments;

public sealed class ExperimentSimulationFactoryTests
{
    [Fact]
    public void Invalid_manifest_and_replicate_indices_are_rejected()
    {
        ExperimentManifest invalidManifest = ExperimentManifestFactory.Create() with { GridWidth = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => ExperimentSimulationFactory.Create(invalidManifest, 0));

        ExperimentManifest manifest = ExperimentManifestFactory.Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => ExperimentSimulationFactory.Create(manifest, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExperimentSimulationFactory.Create(manifest, manifest.ReplicateCount));
    }

    [Fact]
    public void Factory_preserves_old_identity_seeds_and_initial_state_construction()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create();
        const int ReplicateIndex = 1;
        ulong oldReplicateSeed = ReplicateSeedDerivation.Derive(manifest.BaseSeed, ReplicateIndex);
        ulong oldInitialisationSeed = ExperimentSeedDerivation.DeriveInitialisation(oldReplicateSeed);
        ulong oldDynamicsSeed = ExperimentSeedDerivation.DeriveDynamics(oldReplicateSeed);
        PackedAggregateInitialisation oldInitialisation = PackedAggregateInitialiser.Create(manifest, oldInitialisationSeed);
        SerialSimulation oldSimulation = new(
            oldInitialisation.State,
            new Xoshiro256StarStar(oldDynamicsSeed),
            manifest.FluctuationAmplitude);

        ExperimentSimulationInstance actual = ExperimentSimulationFactory.Create(manifest, ReplicateIndex);

        Assert.Equal(manifest.ExperimentId, actual.ExperimentId);
        Assert.Equal("test-sorting-r0002", actual.ReplicateId);
        Assert.Equal(ReplicateIndex, actual.ReplicateIndex);
        Assert.Equal(oldReplicateSeed, actual.ReplicateSeed);
        Assert.Equal(oldInitialisationSeed, actual.InitialisationSeed);
        Assert.Equal(oldDynamicsSeed, actual.DynamicsSeed);
        Assert.Equal(oldSimulation.State.GetCellIdsCopy(), actual.Simulation.State.GetCellIdsCopy());
        Assert.Same(actual.Initialisation.State, actual.Simulation.State);
        Assert.Same(oldSimulation.State, oldInitialisation.State);
        for (int cellId = 1; cellId < actual.Initialisation.State.CellCapacity; cellId++)
        {
            Assert.Equal(oldInitialisation.State.TryGetCellState(cellId, out CellState expected),
                actual.Initialisation.State.TryGetCellState(cellId, out CellState actualCell));
            Assert.Equal(expected, actualCell);
        }
        Assert.Equal(SerialSimulation.KernelId, ExperimentRunner.Run(manifest).Metadata.KernelId);
    }

    [Fact]
    public void Factory_run_matches_experiment_runner_lattice_and_counters()
    {
        ExperimentManifest original = ExperimentManifestFactory.Create();
        ExperimentManifest manifest = original with
        {
            McsCount = 7,
            Measurements = original.Measurements with { SnapshotEveryMcs = 7 }
        };
        const int ReplicateIndex = 2;
        ExperimentSimulationInstance instance = ExperimentSimulationFactory.Create(manifest, ReplicateIndex);
        int accepted = 0;
        int rejected = 0;
        int noOps = 0;
        for (int mcs = 0; mcs < manifest.McsCount; mcs++)
        {
            McsSummary summary = instance.Simulation.RunMcs();
            accepted += summary.Accepted;
            rejected += summary.Rejected;
            noOps += summary.NoOps;
        }

        ExperimentReplicateResult result = ExperimentRunner.RunReplicate(manifest, ReplicateIndex);

        Assert.Equal(ReplicateRunStatus.Succeeded, result.Status);
        Assert.Equal(instance.ReplicateId, result.ReplicateId);
        Assert.Equal(instance.ReplicateSeed, result.Seed);
        Assert.Equal(instance.InitialisationSeed, result.InitialisationSeed);
        Assert.Equal(instance.DynamicsSeed, result.DynamicsSeed);
        Assert.Equal(instance.Simulation.State.GetCellIdsCopy(), result.Snapshots[^1].CellIds);
        Assert.Equal(manifest.GridWidth * manifest.GridHeight * manifest.McsCount, result.AttemptCount);
        Assert.Equal(accepted, result.AcceptedAttemptCount);
        Assert.Equal(rejected, result.RejectedAttemptCount);
        Assert.Equal(noOps, result.NoOpAttemptCount);
        Assert.Equal(instance.Simulation.ConnectivityFallbackCount, result.ConnectivityFallbackCount);
        Assert.Equal(instance.Simulation.CompletedMcs, manifest.McsCount);
    }

}
