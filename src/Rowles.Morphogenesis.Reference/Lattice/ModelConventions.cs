namespace Rowles.Morphogenesis.Reference.Lattice;

public sealed class ModelConventions
{
    public static ModelConventions Canonical { get; } = new(
        CopyNeighbourhood.VonNeumann,
        ContactCouplingNeighbourhood.Moore,
        PerimeterNeighbourhood.Moore,
        ConnectivityAdjacency.VonNeumann);

    public ModelConventions(
        CopyNeighbourhood copyNeighbourhood,
        ContactCouplingNeighbourhood contactCouplingNeighbourhood,
        PerimeterNeighbourhood perimeterNeighbourhood,
        ConnectivityAdjacency connectivityAdjacency)
    {
        CopyNeighbourhood = copyNeighbourhood ?? throw new ArgumentNullException(nameof(copyNeighbourhood));
        ContactCouplingNeighbourhood = contactCouplingNeighbourhood ?? throw new ArgumentNullException(nameof(contactCouplingNeighbourhood));
        PerimeterNeighbourhood = perimeterNeighbourhood ?? throw new ArgumentNullException(nameof(perimeterNeighbourhood));
        ConnectivityAdjacency = connectivityAdjacency ?? throw new ArgumentNullException(nameof(connectivityAdjacency));
    }

    public CopyNeighbourhood CopyNeighbourhood { get; }

    public ContactCouplingNeighbourhood ContactCouplingNeighbourhood { get; }

    public PerimeterNeighbourhood PerimeterNeighbourhood { get; }

    public ConnectivityAdjacency ConnectivityAdjacency { get; }

    internal void ValidateFor(int width, int height)
    {
        ValidateDistinct(CopyNeighbourhood.Offsets, width, height, nameof(CopyNeighbourhood));
        ValidateDistinct(ContactCouplingNeighbourhood.Offsets, width, height, nameof(ContactCouplingNeighbourhood));
        ValidateDistinct(PerimeterNeighbourhood.Offsets, width, height, nameof(PerimeterNeighbourhood));
        ValidateDistinct(ConnectivityAdjacency.Offsets, width, height, nameof(ConnectivityAdjacency));
    }

    private static void ValidateDistinct(ReadOnlySpan<GridPoint> offsets, int width, int height, string role)
    {
        HashSet<int> neighbours = [];
        foreach (GridPoint offset in offsets)
        {
            int wrappedX = PeriodicLattice.Wrap(offset.X, width);
            int wrappedY = PeriodicLattice.Wrap(offset.Y, height);
            int index = wrappedY * width + wrappedX;
            if (index == 0 || !neighbours.Add(index))
            {
                throw new ArgumentException(
                    $"The periodic lattice aliases neighbours for the {role} stencil. " +
                    "Each role must resolve to distinct sites and must not include the centre site.");
            }
        }
    }
}
