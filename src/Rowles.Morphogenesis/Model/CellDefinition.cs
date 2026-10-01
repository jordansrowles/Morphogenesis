using Rowles.Morphogenesis.Diagnostics;
using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Topology;

namespace Rowles.Morphogenesis.Model;

public readonly record struct CellDefinition(
    int CellId,
    int CellTypeId,
    double TargetArea,
    double AreaStiffness,
    double TargetPerimeter,
    double PerimeterStiffness);
