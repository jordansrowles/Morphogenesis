using BenchmarkDotNet.Attributes;
using Rowles.Morphogenesis.Experiments.Execution;

namespace Rowles.Morphogenesis.Benchmarks;

[MemoryDiagnoser]
[InvocationCount(1)]
public class EndToEndReplicateBenchmarks
{
    private M3BenchmarkScenario scenario = null!;

    [ParamsSource(nameof(ScenarioIds))]
    public string ScenarioId { get; set; } = string.Empty;

    public IEnumerable<string> ScenarioIds => M3BenchmarkScenario.All.Select(scenario => scenario.Id);

    [IterationSetup]
    public void ResetScenario() => scenario = M3BenchmarkScenario.Get(ScenarioId);

    [Benchmark]
    public object RunOneNormalExperimentReplicate() =>
        ExperimentRunner.RunReplicate(scenario.Manifest, 0);
}
