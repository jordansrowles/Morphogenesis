using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Random;
using Rowles.Morphogenesis.Topology;

namespace Rowles.Morphogenesis.Dynamics;

public readonly record struct AttemptResult(
    int TargetIndex,
    int SourceIndex,
    int OldCellId,
    int NewCellId,
    AttemptStatus Status,
    RejectionReason RejectionReason,
    HamiltonianBreakdown DeltaH,
    double? AcceptanceProbability,
    double? AcceptanceRandomValue,
    bool UsedGlobalConnectivityFallback);
