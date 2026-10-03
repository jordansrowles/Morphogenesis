namespace Rowles.Morphogenesis.Desktop.Rendering;

public readonly record struct LatticeCoordinate(int X, int Y);

public static class LatticeCoordinates
{
    public static bool TryMapViewToCell(
        double viewX,
        double viewY,
        double viewportWidth,
        double viewportHeight,
        int latticeWidth,
        int latticeHeight,
        double zoom,
        double panX,
        double panY,
        out LatticeCoordinate coordinate)
    {
        coordinate = default;
        if (viewportWidth <= 0 || viewportHeight <= 0 || latticeWidth <= 0 || latticeHeight <= 0 || zoom <= 0)
            return false;
        double fit = Math.Min(viewportWidth / latticeWidth, viewportHeight / latticeHeight);
        double offsetX = (viewportWidth - latticeWidth * fit) / 2;
        double offsetY = (viewportHeight - latticeHeight * fit) / 2;
        double latticeX = (viewX - panX - offsetX * zoom) / (fit * zoom);
        double latticeY = (viewY - panY - offsetY * zoom) / (fit * zoom);
        int x = (int)Math.Floor(latticeX);
        int y = (int)Math.Floor(latticeY);
        if (x < 0 || y < 0 || x >= latticeWidth || y >= latticeHeight)
            return false;
        coordinate = new LatticeCoordinate(x, y);
        return true;
    }
}
