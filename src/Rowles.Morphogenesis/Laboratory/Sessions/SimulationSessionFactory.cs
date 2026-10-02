using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Execution;

namespace Rowles.Morphogenesis.Laboratory.Sessions;

public static class SimulationSessionFactory
{
    public static SimulationSession Create(
        ExperimentManifest manifest,
        int replicateIndex,
        SimulationSessionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();
        SimulationSessionOptions resolvedOptions = options ?? new SimulationSessionOptions();
        resolvedOptions.Validate();
        ExperimentSimulationInstance instance = ExperimentSimulationFactory.Create(manifest, replicateIndex);
        return new SimulationSession(Guid.NewGuid(), manifest, instance, resolvedOptions);
    }
}
