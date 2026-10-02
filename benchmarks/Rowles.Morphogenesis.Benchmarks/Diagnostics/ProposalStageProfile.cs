using System.Diagnostics;

namespace Rowles.Morphogenesis.Benchmarks.Diagnostics;

public enum ProposalStage
{
    ProposalRng,
    BoundaryResolve,
    SameIdCheck,
    FinalSiteConstraint,
    ConnectivityLocal,
    ConnectivityFallback,
    ContactEnergy,
    AreaEnergy,
    PerimeterEnergy,
    Acceptance,
    AcceptedCommit
}

public sealed class ProposalStageProfile
{
    private readonly long[] _ticks = new long[Enum.GetValues<ProposalStage>().Length];
    private readonly long[] _counts = new long[Enum.GetValues<ProposalStage>().Length];

    public void Record(ProposalStage stage, long startTimestamp)
    {
        _ticks[(int)stage] += Stopwatch.GetTimestamp() - startTimestamp;
        _counts[(int)stage]++;
    }

    public long Count(ProposalStage stage) => _counts[(int)stage];
    public double Milliseconds(ProposalStage stage) => _ticks[(int)stage] * 1000.0 / Stopwatch.Frequency;
}
