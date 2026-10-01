namespace Rowles.Morphogenesis.Reference.Lattice;

public readonly record struct GridPoint(int X, int Y)
{
    public static GridPoint operator +(GridPoint point, GridPoint offset) =>
        new(point.X + offset.X, point.Y + offset.Y);

    public override string ToString() => $"({X},{Y})";
}
