namespace Rowles.Morphogenesis.Lattice;

public readonly record struct LatticeConventions(
    CopyNeighbourhood CopyNeighbourhood,
    ContactCouplingNeighbourhood ContactCouplingNeighbourhood,
    PerimeterNeighbourhood PerimeterNeighbourhood,
    ConnectivityAdjacency ConnectivityAdjacency)
{
    public static LatticeConventions Canonical { get; } = new(
        CopyNeighbourhood.VonNeumann,
        ContactCouplingNeighbourhood.Moore,
        PerimeterNeighbourhood.Moore,
        ConnectivityAdjacency.VonNeumann);
}
