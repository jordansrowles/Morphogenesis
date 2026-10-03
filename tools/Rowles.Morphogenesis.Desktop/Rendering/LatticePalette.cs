namespace Rowles.Morphogenesis.Desktop.Rendering;

public enum LatticeViewMode
{
    CellIdentity,
    CellType,
    Boundaries
}

public readonly record struct RgbColour(byte Red, byte Green, byte Blue);

public static class LatticePalette
{
    public static readonly RgbColour Empty = new(14, 20, 29);
    public static readonly RgbColour Boundary = new(244, 247, 255);

    private static readonly RgbColour[] _fixedTypePalette =
    [
        new(54, 82, 196),
        new(224, 112, 58),
        new(41, 154, 112),
        new(181, 77, 154),
        new(220, 177, 57),
        new(52, 156, 190),
        new(199, 78, 76),
        new(121, 101, 181)
    ];

    public static RgbColour ForIdentity(int cellId)
    {
        if (cellId == 0)
            return Empty;
        uint hash = unchecked((uint)cellId) * 2654435761u;
        return HslToRgb(hash % 360, 0.64, 0.54);
    }

    public static RgbColour ForType(int typeId)
    {
        if (typeId <= 0)
            return Empty;
        if (typeId <= _fixedTypePalette.Length)
            return _fixedTypePalette[typeId - 1];
        double hue = typeId * 137.508 % 360;
        return HslToRgb(hue, 0.68, 0.54);
    }

    private static RgbColour HslToRgb(double hue, double saturation, double lightness)
    {
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double sector = hue / 60;
        double secondary = chroma * (1 - Math.Abs(sector % 2 - 1));
        double red;
        double green;
        double blue;
        if (sector < 1) (red, green, blue) = (chroma, secondary, 0);
        else if (sector < 2) (red, green, blue) = (secondary, chroma, 0);
        else if (sector < 3) (red, green, blue) = (0, chroma, secondary);
        else if (sector < 4) (red, green, blue) = (0, secondary, chroma);
        else if (sector < 5) (red, green, blue) = (secondary, 0, chroma);
        else (red, green, blue) = (chroma, 0, secondary);

        double offset = lightness - chroma / 2;
        return new RgbColour(
            (byte)Math.Round((red + offset) * 255, MidpointRounding.AwayFromZero),
            (byte)Math.Round((green + offset) * 255, MidpointRounding.AwayFromZero),
            (byte)Math.Round((blue + offset) * 255, MidpointRounding.AwayFromZero));
    }
}
