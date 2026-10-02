namespace Rowles.Morphogenesis.Laboratory.Sessions;

public sealed record CellInspection(
    long Mcs,
    int CellId,
    int CellTypeId,
    string CellTypeName,
    int Area,
    int Perimeter,
    double TargetArea,
    double AreaStiffness,
    double TargetPerimeter,
    double PerimeterStiffness);
