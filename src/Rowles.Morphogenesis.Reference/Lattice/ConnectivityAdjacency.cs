namespace Rowles.Morphogenesis.Reference.Lattice;

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
