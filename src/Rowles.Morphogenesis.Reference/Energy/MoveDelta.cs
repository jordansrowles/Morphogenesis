using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Model;

namespace Rowles.Morphogenesis.Reference.Energy;

public readonly record struct MoveDelta(
    GridPoint Source,
    GridPoint Target,
    int OldCellId,
    int NewCellId,
    HamiltonianBreakdown Terms)
{
    public double Total => Terms.Total;
}
