using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Initialisation;
using Rowles.Morphogenesis.Tests.Baseline;

namespace Rowles.Morphogenesis.Benchmarks;

[MemoryDiagnoser]
[InvocationCount(1)]
public class CanonicalPlanBenchmarks
{
    private M3BenchmarkScenario _scenario = null!;
    private EntrySerialSimulation _entry = null!;
    private SerialSimulation _planned = null!;

    [ParamsSource(nameof(ScenarioIds))]
    public string ScenarioId { get; set; } = string.Empty;
    public IEnumerable<string> ScenarioIds => M3BenchmarkScenario.All.Select(item => item.Id);

    [GlobalSetup(Target = nameof(Entry))]
    public void WarmEntryBody()
    {
        Reset();
        Stopwatch timer = Stopwatch.StartNew();
        int calls = 0;
        do { Entry(); calls++; } while (timer.ElapsedMilliseconds < 2000 || calls < 64);
    }

    [GlobalSetup(Target = nameof(Planned))]
    public void WarmPlannedBody()
    {
        Reset();
        Stopwatch timer = Stopwatch.StartNew();
        int calls = 0;
        do { Planned(); calls++; } while (timer.ElapsedMilliseconds < 2000 || calls < 64);
    }

    [IterationSetup]
    public void Reset()
    {
        _scenario = M3BenchmarkScenario.Get(ScenarioId);
        var manifest = _scenario.Manifest;
        _entry = new EntrySerialSimulation(PackedAggregateInitialiser.Create(manifest, manifest.BaseSeed).State,
            manifest.BaseSeed, manifest.FluctuationAmplitude);
        _planned = new SerialSimulation(PackedAggregateInitialiser.Create(manifest, manifest.BaseSeed).State,
            manifest.BaseSeed, manifest.FluctuationAmplitude);
    }

    [Benchmark(Baseline = true)]
    public KernelBatchSummary Entry()
    {
        int accepted = 0, rejected = 0, noOps = 0, fallbacks = 0;
        for (int mcs = 0; mcs < _scenario.KernelMcsPerInvocation; mcs++)
        {
            McsSummary summary = _entry.RunMcs();
            accepted += summary.Accepted; rejected += summary.Rejected;
            noOps += summary.NoOps; fallbacks += summary.ConnectivityFallbacks;
        }
        return new KernelBatchSummary(accepted, rejected, noOps, fallbacks);
    }

    [Benchmark]
    public KernelBatchSummary Planned()
    {
        int accepted = 0, rejected = 0, noOps = 0, fallbacks = 0;
        for (int mcs = 0; mcs < _scenario.KernelMcsPerInvocation; mcs++)
        {
            McsSummary summary = _planned.RunMcs();
            accepted += summary.Accepted; rejected += summary.Rejected;
            noOps += summary.NoOps; fallbacks += summary.ConnectivityFallbacks;
        }
        return new KernelBatchSummary(accepted, rejected, noOps, fallbacks);
    }
}
