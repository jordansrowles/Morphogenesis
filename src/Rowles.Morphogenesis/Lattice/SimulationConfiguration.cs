namespace Rowles.Morphogenesis.Lattice;

public enum BoundaryMode
{
    Periodic,
    Wall
}

public enum CopyNeighbourhood
{
    VonNeumann
}

public enum ContactCouplingNeighbourhood
{
    VonNeumann,
    Moore
}

public enum PerimeterNeighbourhood
{
    VonNeumann,
    Moore
}

public enum ConnectivityAdjacency
{
    VonNeumann
}

public readonly record struct LatticeConventions(
    CopyNeighbourhood CopyNeighbourhood,
    ContactCouplingNeighbourhood ContactCouplingNeighbourhood,
    PerimeterNeighbourhood PerimeterNeighbourhood,
    ConnectivityAdjacency ConnectivityAdjacency)
{
    public static LatticeConventions Canonical { get; } = new(
        CopyNeighbourhood.VonNeumann,
        ContactCouplingNeighbourhood.Moore,
        PerimeterNeighbourhood.Moore,
        ConnectivityAdjacency.VonNeumann);
}

public readonly record struct SimulationConfiguration(
    BoundaryMode BoundaryMode,
    LatticeConventions Conventions)
{
    public static SimulationConfiguration PeriodicCanonical { get; } = new(
        BoundaryMode.Periodic,
        LatticeConventions.Canonical);

    public static SimulationConfiguration WallCanonical { get; } = new(
        BoundaryMode.Wall,
        LatticeConventions.Canonical);
}

internal static class StencilGeometry
{
    internal const int VonNeumannCount = 4;
    internal const int MooreCount = 8;

    internal static int Count(CopyNeighbourhood _) => VonNeumannCount;

    internal static int Count(ContactCouplingNeighbourhood neighbourhood) => neighbourhood switch
    {
        ContactCouplingNeighbourhood.VonNeumann => VonNeumannCount,
        ContactCouplingNeighbourhood.Moore => MooreCount,
        _ => throw new ArgumentOutOfRangeException(nameof(neighbourhood))
    };

    internal static int Count(PerimeterNeighbourhood neighbourhood) => neighbourhood switch
    {
        PerimeterNeighbourhood.VonNeumann => VonNeumannCount,
        PerimeterNeighbourhood.Moore => MooreCount,
        _ => throw new ArgumentOutOfRangeException(nameof(neighbourhood))
    };

    internal static int Count(ConnectivityAdjacency _) => VonNeumannCount;

    internal static void CopyOffset(CopyNeighbourhood _, int index, out int x, out int y) =>
        VonNeumannOffset(index, out x, out y);

    internal static void ConnectivityOffset(ConnectivityAdjacency _, int index, out int x, out int y) =>
        VonNeumannOffset(index, out x, out y);

    internal static void ContactOffset(ContactCouplingNeighbourhood neighbourhood, int index, out int x, out int y)
    {
        if (neighbourhood == ContactCouplingNeighbourhood.VonNeumann)
        {
            VonNeumannOffset(index, out x, out y);
            return;
        }

        MooreOffset(index, out x, out y);
    }

    internal static void PerimeterOffset(PerimeterNeighbourhood neighbourhood, int index, out int x, out int y)
    {
        if (neighbourhood == PerimeterNeighbourhood.VonNeumann)
        {
            VonNeumannOffset(index, out x, out y);
            return;
        }

        MooreOffset(index, out x, out y);
    }

    internal static bool IsPositiveHalf(int x, int y) => x > 0 || (x == 0 && y > 0);

    private static void VonNeumannOffset(int index, out int x, out int y)
    {
        (x, y) = index switch
        {
            0 => (0, -1),
            1 => (1, 0),
            2 => (0, 1),
            3 => (-1, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    internal static void MooreOffset(int index, out int x, out int y)
    {
        (x, y) = index switch
        {
            0 => (-1, -1),
            1 => (0, -1),
            2 => (1, -1),
            3 => (-1, 0),
            4 => (1, 0),
            5 => (-1, 1),
            6 => (0, 1),
            7 => (1, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }
}
