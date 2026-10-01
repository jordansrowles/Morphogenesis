using Rowles.Morphogenesis.Lattice;

namespace Rowles.Morphogenesis.Experiments.Configuration;

public sealed record MeasurementConfiguration
{
    public required int EveryMcs { get; init; }

    public required bool IncludeMcsZero { get; init; }

    public required ContactCouplingNeighbourhood InterfaceNeighbourhood { get; init; }

    public required int SnapshotEveryMcs { get; init; }

    public required int ValidateInvariantsEveryMcs { get; init; }
}
