using BenchmarkDotNet.Attributes;
using Rowles.Morphogenesis.Initialisation;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Snapshots;

namespace Rowles.Morphogenesis.Benchmarks;

[MemoryDiagnoser]
public class SnapshotBenchmarks
{
    private M3BenchmarkScenario scenario = null!;
    private MorphogenesisState state = null!;

    [ParamsSource(nameof(ScenarioIds))]
    public string ScenarioId { get; set; } = string.Empty;

    public IEnumerable<string> ScenarioIds =>
    [
        "B00-sparse-single-cell",
        "B01-e02-control",
        "B05-scale-256"
    ];

    [GlobalSetup]
    public void FreezeInitialState()
    {
        scenario = M3BenchmarkScenario.Get(ScenarioId);
        state = PackedAggregateInitialiser.Create(scenario.Manifest, scenario.Manifest.BaseSeed).State;
    }

    [Benchmark]
    public ExperimentSnapshot CaptureSnapshot() => ExperimentSnapshot.Capture(
        scenario.Manifest,
        "m3-benchmark-r0001",
        0,
        scenario.Manifest.BaseSeed,
        0,
        state);
}
