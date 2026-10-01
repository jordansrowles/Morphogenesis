using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Energy;

public readonly record struct MoveEvaluation(
    HamiltonianBreakdown Terms,
    int OldCellPerimeterDelta,
    int NewCellPerimeterDelta)
{
    public double Total => Terms.Total;
}
