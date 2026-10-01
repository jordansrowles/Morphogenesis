using Rowles.Morphogenesis.Reference.Energy;
using Rowles.Morphogenesis.Reference.Lattice;

namespace Rowles.Morphogenesis.Reference.Dynamics;

public enum AttemptStatus
{
    NoOp,
    Rejected,
    Accepted
}

public readonly record struct AttemptResult(
    GridPoint Target,
    GridPoint Source,
    int OldCellId,
    int NewCellId,
    AttemptStatus Status,
    HamiltonianBreakdown DeltaH,
    double? AcceptanceProbability,
    double? AcceptanceRandomValue);
