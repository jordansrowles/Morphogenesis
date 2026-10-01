using Rowles.Morphogenesis.Diagnostics;
using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Topology;

namespace Rowles.Morphogenesis.Model;

public readonly record struct CellState(
    int CellId,
    int CellTypeId,
    bool IsAlive,
    int Area,
    int Perimeter,
    double TargetArea,
    double AreaStiffness,
    double TargetPerimeter,
    double PerimeterStiffness);
