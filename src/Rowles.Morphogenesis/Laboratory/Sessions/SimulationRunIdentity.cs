namespace Rowles.Morphogenesis.Laboratory.Sessions;

public sealed record SimulationRunIdentity(
    string ExperimentId,
    string ReplicateId,
    int ReplicateIndex,
    ulong ReplicateSeed,
    ulong InitialisationSeed,
    ulong DynamicsSeed,
    string KernelId);
