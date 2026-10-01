namespace Rowles.Morphogenesis.Reference.Lattice;

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
