namespace Rowles.Morphogenesis.Desktop.Rendering;

public static class BoundaryOverlay
{
    public static void FillMask(ReadOnlySpan<int> cellIds, int width, int height, string boundaryMode, Span<bool> mask)
    {
        int cellCount = GetCellCount(width, height);
        if (cellIds.Length != cellCount)
            throw new ArgumentException("Cell count does not match the supplied dimensions.", nameof(cellIds));
        if (mask.Length != cellCount)
            throw new ArgumentException("Mask length does not match the supplied dimensions.", nameof(mask));
        bool periodic = string.Equals(boundaryMode, "Periodic", StringComparison.OrdinalIgnoreCase);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                int cellId = cellIds[index];
                mask[index] = cellId > 0 && HasDifferentNeighbour(cellIds, width, height, x, y, cellId, periodic);
            }
        }
    }

    private static bool HasDifferentNeighbour(ReadOnlySpan<int> cellIds, int width, int height, int x, int y, int cellId, bool periodic)
    {
        if (HasDifferent(cellIds, width, height, x - 1, y, cellId, periodic) ||
            HasDifferent(cellIds, width, height, x + 1, y, cellId, periodic) ||
            HasDifferent(cellIds, width, height, x, y - 1, cellId, periodic) ||
            HasDifferent(cellIds, width, height, x, y + 1, cellId, periodic))
            return true;
        return false;
    }

    private static bool HasDifferent(ReadOnlySpan<int> cellIds, int width, int height, int x, int y, int cellId, bool periodic)
    {
        if (periodic)
        {
            x = (x + width) % width;
            y = (y + height) % height;
        }
        else if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return false;
        }
        return cellIds[y * width + x] != cellId;
    }

    private static int GetCellCount(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Lattice dimensions must be positive.");
        return checked(width * height);
    }
}
