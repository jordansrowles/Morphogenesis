namespace Rowles.Morphogenesis.Reference.Lattice;

public static class PeriodicLattice
{
    public static int Wrap(int coordinate, int length)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Periodic dimensions must be positive.");
        }

        int wrapped = coordinate % length;
        return wrapped < 0 ? wrapped + length : wrapped;
    }

    public static GridPoint Resolve(GridPoint point, GridPoint offset, int width, int height) =>
        new(Wrap(point.X + offset.X, width), Wrap(point.Y + offset.Y, height));
}
