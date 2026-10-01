namespace Rowles.Morphogenesis.Reference.Lattice;

public sealed class CopyNeighbourhood
{
    private static readonly GridPoint[] VonNeumannOffsets =
    [
        new(0, -1),
        new(1, 0),
        new(0, 1),
        new(-1, 0)
    ];

    private CopyNeighbourhood(string name) => Name = name;

    public static CopyNeighbourhood VonNeumann { get; } = new("Von Neumann, four neighbours");

    public string Name { get; }

    public int Count => VonNeumannOffsets.Length;

    internal ReadOnlySpan<GridPoint> Offsets => VonNeumannOffsets;
}

public sealed class ContactCouplingNeighbourhood
{
    private static readonly GridPoint[] VonNeumannOffsets =
    [
        new(0, -1),
        new(1, 0),
        new(0, 1),
        new(-1, 0)
    ];

    private static readonly GridPoint[] VonNeumannPositiveHalf =
    [
        new(1, 0),
        new(0, 1)
    ];

    private static readonly GridPoint[] MooreOffsets =
    [
        new(-1, -1), new(0, -1), new(1, -1),
        new(-1, 0),                    new(1, 0),
        new(-1, 1),  new(0, 1),  new(1, 1)
    ];

    private static readonly GridPoint[] MoorePositiveHalf =
    [
        new(1, -1),
        new(1, 0),
        new(1, 1),
        new(0, 1)
    ];

    private ContactCouplingNeighbourhood(string name, GridPoint[] offsets, GridPoint[] positiveHalf) =>
        (Name, Offsets, PositiveHalf) = (name, offsets, positiveHalf);

    public static ContactCouplingNeighbourhood VonNeumann { get; } =
        new("Von Neumann, four neighbours", VonNeumannOffsets, VonNeumannPositiveHalf);

    public static ContactCouplingNeighbourhood Moore { get; } =
        new("Moore, eight neighbours", MooreOffsets, MoorePositiveHalf);

    public string Name { get; }

    internal GridPoint[] Offsets { get; }

    internal GridPoint[] PositiveHalf { get; }
}

public sealed class PerimeterNeighbourhood
{
    private static readonly GridPoint[] MooreOffsets =
    [
        new(-1, -1), new(0, -1), new(1, -1),
        new(-1, 0),                    new(1, 0),
        new(-1, 1),  new(0, 1),  new(1, 1)
    ];

    private PerimeterNeighbourhood(string name) => Name = name;

    public static PerimeterNeighbourhood Moore { get; } = new("Moore, eight neighbours");

    public string Name { get; }

    internal ReadOnlySpan<GridPoint> Offsets => MooreOffsets;
}

public sealed class ConnectivityAdjacency
{
    private static readonly GridPoint[] VonNeumannOffsets =
    [
        new(0, -1),
        new(1, 0),
        new(0, 1),
        new(-1, 0)
    ];

    private ConnectivityAdjacency(string name) => Name = name;

    public static ConnectivityAdjacency VonNeumann { get; } = new("Von Neumann, four neighbours");

    public string Name { get; }

    internal ReadOnlySpan<GridPoint> Offsets => VonNeumannOffsets;
}
