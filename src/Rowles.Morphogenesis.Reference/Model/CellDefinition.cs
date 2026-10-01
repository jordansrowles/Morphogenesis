namespace Rowles.Morphogenesis.Reference.Model;

public sealed record CellDefinition(
    int CellId,
    int CellTypeId,
    double TargetArea,
    double AreaStiffness,
    double TargetPerimeter,
    double PerimeterStiffness);
