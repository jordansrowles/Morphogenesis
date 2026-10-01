using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Model;

namespace Rowles.Morphogenesis.Reference.Energy;

public readonly record struct BruteForceMove(
    GridPoint Source,
    GridPoint Target,
    int OldCellId,
    int NewCellId,
    HamiltonianBreakdown Before,
    HamiltonianBreakdown After)
{
    public HamiltonianBreakdown Terms => After - Before;

    public double TotalDelta => Terms.Total;
}
