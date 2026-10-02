using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Dynamics.Acceleration;
using Rowles.Morphogenesis.Initialisation;

namespace Rowles.Morphogenesis.Benchmarks;

[MemoryDiagnoser]
[InvocationCount(1)]
public class ProposalSpaceBenchmarks
{
    private M3BenchmarkScenario _scenario = null!;
    private SerialSimulation _canonical = null!;
    private EventClockSimulation _border = null!;
    private EventClockSimulation _directed = null!;

    [ParamsSource(nameof(ScenarioIds))]
    public string ScenarioId { get; set; } = string.Empty;
    public IEnumerable<string> ScenarioIds => M3BenchmarkScenario.All.Select(item => item.Id);

    [GlobalSetup(Target = nameof(Canonical))]
    public void WarmCanonicalBody()
    {
        Reset();
        Stopwatch timer = Stopwatch.StartNew();
        int calls = 0;
        do { Canonical(); calls++; } while (timer.ElapsedMilliseconds < 2000 || calls < 64);
    }

    [GlobalSetup(Target = nameof(BorderSites))]
    public void WarmBorderBody()
    {
        Reset();
        Stopwatch timer = Stopwatch.StartNew();
        int calls = 0;
        do { BorderSites(); calls++; } while (timer.ElapsedMilliseconds < 2000 || calls < 64);
    }

    [GlobalSetup(Target = nameof(DirectedInterface))]
    public void WarmDirectedBody()
    {
        Reset();
        Stopwatch timer = Stopwatch.StartNew();
        int calls = 0;
        do { DirectedInterface(); calls++; } while (timer.ElapsedMilliseconds < 2000 || calls < 64);
    }

    [IterationSetup]
    public void Reset()
    {
        _scenario = M3BenchmarkScenario.Get(ScenarioId);
        var manifest = _scenario.Manifest;
        _canonical = new SerialSimulation(PackedAggregateInitialiser.Create(manifest, manifest.BaseSeed).State,
            manifest.BaseSeed, manifest.FluctuationAmplitude);
        _border = new EventClockSimulation(PackedAggregateInitialiser.Create(manifest, manifest.BaseSeed).State,
            manifest.BaseSeed, manifest.FluctuationAmplitude, ProposalSpaceKind.BorderSites);
        _directed = new EventClockSimulation(PackedAggregateInitialiser.Create(manifest, manifest.BaseSeed).State,
            manifest.BaseSeed, manifest.FluctuationAmplitude, ProposalSpaceKind.DirectedInterface);
    }

    [Benchmark(Baseline = true)]
    public KernelBatchSummary Canonical()
    {
        int accepted = 0, rejected = 0, noOps = 0, fallbacks = 0;
        for (int mcs = 0; mcs < _scenario.KernelMcsPerInvocation; mcs++)
        {
            McsSummary summary = _canonical.RunMcs();
            accepted += summary.Accepted; rejected += summary.Rejected;
            noOps += summary.NoOps; fallbacks += summary.ConnectivityFallbacks;
        }
        return new KernelBatchSummary(accepted, rejected, noOps, fallbacks);
    }

    [Benchmark]
    public KernelBatchSummary BorderSites() => Run(_border);

    [Benchmark]
    public KernelBatchSummary DirectedInterface() => Run(_directed);

    private KernelBatchSummary Run(EventClockSimulation simulation)
    {
        int accepted = 0, rejected = 0, noOps = 0, fallbacks = 0;
        for (int mcs = 0; mcs < _scenario.KernelMcsPerInvocation; mcs++)
        {
            McsSummary summary = simulation.RunMcs();
            accepted += summary.Accepted; rejected += summary.Rejected;
            noOps += summary.NoOps; fallbacks += summary.ConnectivityFallbacks;
        }
        return new KernelBatchSummary(accepted, rejected, noOps, fallbacks);
    }
}
