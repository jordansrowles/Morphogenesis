namespace Rowles.Morphogenesis.Lattice;

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
