namespace Rowles.Morphogenesis.Reference.Lattice;

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
