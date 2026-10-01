using BenchmarkDotNet.Attributes;
using Rowles.Morphogenesis.Initialisation;
using Rowles.Morphogenesis.Measurements;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Benchmarks;

[MemoryDiagnoser]
public class MeasurementBenchmarks
{
    private M3BenchmarkScenario scenario = null!;
    private MorphogenesisState state = null!;

    [ParamsSource(nameof(ScenarioIds))]
    public string ScenarioId { get; set; } = string.Empty;

    public IEnumerable<string> ScenarioIds => M3BenchmarkScenario.All.Select(scenario => scenario.Id);

    [GlobalSetup]
    public void FreezeInitialState()
    {
        scenario = M3BenchmarkScenario.Get(ScenarioId);
        state = PackedAggregateInitialiser.Create(scenario.Manifest, scenario.Manifest.BaseSeed).State;
    }

    [Benchmark]
    public TissueMeasurements MeasureFrozenState() => TissueMeasurementCalculator.Measure(
        state,
        scenario.Manifest.Initialiser.TypeAId,
        scenario.Manifest.Initialiser.TypeBId,
        scenario.Manifest.Measurements.InterfaceNeighbourhood);
}
