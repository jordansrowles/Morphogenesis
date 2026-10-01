using Rowles.Morphogenesis.Lattice;

namespace Rowles.Morphogenesis.Model;

public readonly record struct CellReplayParameters(
    int CellId,
    int CellTypeId,
    double TargetArea,
    double AreaStiffness,
    double TargetPerimeter,
    double PerimeterStiffness);
