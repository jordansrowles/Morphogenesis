using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Rowles.Morphogenesis.Experiments.Execution;

namespace Rowles.Morphogenesis.Benchmarks;

[MemoryDiagnoser]
[InvocationCount(1)]
public class EndToEndReplicateBenchmarks
{
    private M3BenchmarkScenario _scenario = null!;

    [ParamsSource(nameof(ScenarioIds))]
    public string ScenarioId { get; set; } = string.Empty;

    public IEnumerable<string> ScenarioIds => M3BenchmarkScenario.All.Select(scenario => scenario.Id);

    [GlobalSetup]
    public void WarmRunner()
    {
        ResetScenario();
        Stopwatch timer = Stopwatch.StartNew();
        int calls = 0;
        do
        {
            RunOneNormalExperimentReplicate();
            calls++;
        }
        while (timer.ElapsedMilliseconds < 2000 || calls < 64);
    }

    [IterationSetup]
    public void ResetScenario() => _scenario = M3BenchmarkScenario.Get(ScenarioId);

    [Benchmark]
    public object RunOneNormalExperimentReplicate() =>
        ExperimentRunner.RunReplicate(_scenario.Manifest, 0);
}
