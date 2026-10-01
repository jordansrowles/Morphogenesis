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
