using BenchmarkDotNet.Attributes;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Initialisation;
using Rowles.Morphogenesis.Random;

namespace Rowles.Morphogenesis.Benchmarks;

[MemoryDiagnoser]
[InvocationCount(1)]
public class CanonicalKernelBenchmarks
{
    private M3BenchmarkScenario scenario = null!;
    private SerialSimulation simulation = null!;

    [ParamsSource(nameof(ScenarioIds))]
    public string ScenarioId { get; set; } = string.Empty;

    public IEnumerable<string> ScenarioIds => M3BenchmarkScenario.All.Select(scenario => scenario.Id);

    [IterationSetup]
    public void ResetCanonicalState()
    {
        scenario = M3BenchmarkScenario.Get(ScenarioId);
        PackedAggregateInitialisation initialisation = PackedAggregateInitialiser.Create(
            scenario.Manifest, scenario.Manifest.BaseSeed);
        simulation = new SerialSimulation(initialisation.State, scenario.Manifest.BaseSeed,
            scenario.Manifest.FluctuationAmplitude);
    }

    [Benchmark]
    public KernelBatchSummary RunCanonicalMcsBatch()
    {
        int accepted = 0;
        int rejected = 0;
        int noOps = 0;
        int fallbacks = 0;
        for (int mcs = 0; mcs < scenario.KernelMcsPerInvocation; mcs++)
        {
            McsSummary summary = simulation.RunMcs();
            accepted += summary.Accepted;
            rejected += summary.Rejected;
            noOps += summary.NoOps;
            fallbacks += summary.ConnectivityFallbacks;
        }

        return new KernelBatchSummary(accepted, rejected, noOps, fallbacks);
    }
}

public readonly record struct KernelBatchSummary(int Accepted, int Rejected, int NoOps, int ConnectivityFallbacks);
