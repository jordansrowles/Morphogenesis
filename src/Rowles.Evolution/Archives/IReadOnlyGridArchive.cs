namespace Rowles.Evolution.Archives;

/// <summary>Read-only view of a Grid MAP-Elites archive.</summary>
public interface IReadOnlyGridArchive
{
    GridArchiveConfiguration Configuration { get; }
    int Occupancy { get; }
    double Coverage { get; }
    IEnumerable<GridElite> OccupiedElites { get; }
    GridElite? GetAt(int cellIndex);
}
