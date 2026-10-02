using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Dynamics;

[Flags]
internal enum BuiltInEnergyTerms
{
    Contact = 1,
    Area = 2,
    Perimeter = 4
}

[Flags]
internal enum BuiltInHardConstraints
{
    FinalSite = 1,
    Connectivity = 2,
    FixedWall = 4
}

internal readonly record struct NeighbourOffset(int Dx, int Dy);

/// <summary>Validated immutable built-in execution data, resolved once outside proposal evaluation.</summary>
internal sealed class KernelPlan
{
    private static readonly NeighbourOffset[] _vonNeumann = CreateOffsets(ContactCouplingNeighbourhood.VonNeumann);
    private static readonly NeighbourOffset[] _moore = CreateOffsets(ContactCouplingNeighbourhood.Moore);
    private readonly NeighbourOffset[] _contactOffsets;
    private readonly NeighbourOffset[] _perimeterOffsets;
    private readonly ContactEnergyMatrix _contactEnergies;

    internal KernelPlan(SimulationConfiguration configuration, ContactEnergyMatrix contactEnergies)
    {
        _contactEnergies = contactEnergies;
        ContactTypeCount = contactEnergies.TypeCount;
        _contactOffsets = configuration.Conventions.ContactCouplingNeighbourhood == ContactCouplingNeighbourhood.Moore ? _moore : _vonNeumann;
        _perimeterOffsets = configuration.Conventions.PerimeterNeighbourhood == PerimeterNeighbourhood.Moore ? _moore : _vonNeumann;
        HardConstraints = BuiltInHardConstraints.FinalSite | BuiltInHardConstraints.Connectivity;
        if (configuration.BoundaryMode == BoundaryMode.Wall) HardConstraints |= BuiltInHardConstraints.FixedWall;
    }

    // Per-cell targets and stiffnesses remain cell-local; zero stiffness does not disable arithmetic.
    internal BuiltInEnergyTerms EnergyTerms => BuiltInEnergyTerms.Contact | BuiltInEnergyTerms.Area | BuiltInEnergyTerms.Perimeter;
    internal BuiltInHardConstraints HardConstraints { get; }
    internal int ContactTypeCount { get; }
    internal ReadOnlySpan<double> ContactValues => _contactEnergies.Values;
    internal ReadOnlySpan<NeighbourOffset> ContactOffsets => _contactOffsets;
    internal ReadOnlySpan<NeighbourOffset> PerimeterOffsets => _perimeterOffsets;

    private static NeighbourOffset[] CreateOffsets(ContactCouplingNeighbourhood neighbourhood)
    {
        NeighbourOffset[] offsets = new NeighbourOffset[StencilGeometry.Count(neighbourhood)];
        for (int index = 0; index < offsets.Length; index++)
        {
            StencilGeometry.ContactOffset(neighbourhood, index, out int dx, out int dy);
            offsets[index] = new NeighbourOffset(dx, dy);
        }
        return offsets;
    }
}
