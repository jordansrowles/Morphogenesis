using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Experiments.Random;
using Rowles.Morphogenesis.Initialisation;
using Rowles.Morphogenesis.Random;

namespace Rowles.Morphogenesis.Experiments.Execution;

public static class ExperimentSimulationFactory
{
    public static ExperimentSimulationInstance Create(
        ExperimentManifest manifest,
        int replicateIndex)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();
        if ((uint)replicateIndex >= (uint)manifest.ReplicateCount)
        {
            throw new ArgumentOutOfRangeException(nameof(replicateIndex));
        }

        string replicateId = $"{manifest.ExperimentId}-r{replicateIndex + 1:D4}";
        ulong replicateSeed = ReplicateSeedDerivation.Derive(manifest.BaseSeed, replicateIndex);
        ulong initialisationSeed = ExperimentSeedDerivation.DeriveInitialisation(replicateSeed);
        ulong dynamicsSeed = ExperimentSeedDerivation.DeriveDynamics(replicateSeed);
        PackedAggregateInitialisation initialisation = PackedAggregateInitialiser.Create(manifest, initialisationSeed);
        SerialSimulation simulation = new(
            initialisation.State,
            new Xoshiro256StarStar(dynamicsSeed),
            manifest.FluctuationAmplitude);

        return new ExperimentSimulationInstance(
            manifest.ExperimentId,
            replicateId,
            replicateIndex,
            replicateSeed,
            initialisationSeed,
            dynamicsSeed,
            initialisation,
            simulation);
    }
}
