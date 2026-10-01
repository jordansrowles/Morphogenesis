using Rowles.Morphogenesis.Diagnostics;
using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Topology;

namespace Rowles.Morphogenesis.Model;

internal struct CellRuntime
{
    internal int CellTypeId;
    internal bool IsAlive;
    internal int Area;
    internal int Perimeter;
    internal double TargetArea;
    internal double AreaStiffness;
    internal double TargetPerimeter;
    internal double PerimeterStiffness;
}
