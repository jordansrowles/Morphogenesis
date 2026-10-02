using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Initialisation;

namespace Rowles.Morphogenesis.Experiments.Execution;

public sealed record ExperimentSimulationInstance(
    string ExperimentId,
    string ReplicateId,
    int ReplicateIndex,
    ulong ReplicateSeed,
    ulong InitialisationSeed,
    ulong DynamicsSeed,
    PackedAggregateInitialisation Initialisation,
    SerialSimulation Simulation);
