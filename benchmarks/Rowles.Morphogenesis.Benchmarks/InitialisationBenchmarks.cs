using BenchmarkDotNet.Attributes;
using Rowles.Morphogenesis.Initialisation;

namespace Rowles.Morphogenesis.Benchmarks;

[MemoryDiagnoser]
public class InitialisationBenchmarks
{
    private M3BenchmarkScenario scenario = null!;

    [ParamsSource(nameof(ScenarioIds))]
    public string ScenarioId { get; set; } = string.Empty;

    public IEnumerable<string> ScenarioIds => M3BenchmarkScenario.All.Select(scenario => scenario.Id);

    [GlobalSetup]
    public void SelectScenario() => scenario = M3BenchmarkScenario.Get(ScenarioId);

    [Benchmark]
    public PackedAggregateInitialisation CreatePackedAggregate() =>
        PackedAggregateInitialiser.Create(scenario.Manifest, scenario.Manifest.BaseSeed);
}
