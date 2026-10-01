namespace Rowles.Morphogenesis.Snapshots;

public sealed record SnapshotCellState(
    int CellId,
    int CellTypeId,
    bool IsAlive,
    int Area,
    int Perimeter,
    double TargetArea,
    double AreaStiffness,
    double TargetPerimeter,
    double PerimeterStiffness);
