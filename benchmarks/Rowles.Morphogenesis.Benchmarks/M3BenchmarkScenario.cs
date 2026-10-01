using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Configuration;
using Rowles.Morphogenesis.Lattice;

namespace Rowles.Morphogenesis.Benchmarks;

internal sealed record M3BenchmarkScenario(string Id, ExperimentManifest Manifest, int KernelMcsPerInvocation)
{
    public static M3BenchmarkScenario[] All { get; } = CreateAll();

    public static M3BenchmarkScenario Get(string id) =>
        All.Single(scenario => scenario.Id.Equals(id, StringComparison.Ordinal));

    private static M3BenchmarkScenario[] CreateAll()
    {
        string scenarios = Path.Combine(AppContext.BaseDirectory, "Scenarios");
        ExperimentManifest b00 = ExperimentManifest.ReadJson(Path.Combine(scenarios, "E00-single-cell-relaxation.json"));
        ExperimentManifest b01 = ExperimentManifest.ReadJson(Path.Combine(scenarios, "E02-control.json"));
        ExperimentManifest b02 = ExperimentManifest.ReadJson(Path.Combine(scenarios, "E02-sorting.json"));
        ExperimentManifest b03 = ExperimentManifest.ReadJson(Path.Combine(scenarios, "E01-fluctuation-high.json"));
        ExperimentManifest b04 = Scale(b01, "B04-confluent", 32, 64, 20, BoundaryMode.Periodic);
        ExperimentManifest b05a = Scale(b01, "B05-scale-64", 64, 64, 1, BoundaryMode.Periodic);
        ExperimentManifest b05b = Scale(b01, "B05-scale-128", 128, 256, 1, BoundaryMode.Periodic);
        ExperimentManifest b05c = Scale(b01, "B05-scale-256", 256, 1024, 1, BoundaryMode.Periodic);
        ExperimentManifest b06 = Scale(b01, "B06-wall-control", 32, 16, 80, BoundaryMode.Wall);

        return
        [
            new("B00-sparse-single-cell", b00, 500),
            new("B01-e02-control", b01, 200),
            new("B02-e02-sorting", b02, 200),
            new("B03-high-fluctuation", b03, 200),
            new("B04-confluent-synthetic", b04, 100),
            new("B05-scale-64", b05a, 100),
            new("B05-scale-128", b05b, 20),
            new("B05-scale-256", b05c, 20),
            new("B06-wall-control", b06, 1000)
        ];
    }

    private static ExperimentManifest Scale(ExperimentManifest baseline, string id, int gridSize,
        int cellCount, int mcsCount, BoundaryMode boundaryMode)
    {
        MeasurementConfiguration measurements = new()
        {
            EveryMcs = Math.Max(1, mcsCount),
            IncludeMcsZero = true,
            InterfaceNeighbourhood = baseline.Measurements.InterfaceNeighbourhood,
            SnapshotEveryMcs = 0,
            ValidateInvariantsEveryMcs = Math.Max(1, mcsCount)
        };

        return baseline with
        {
            ExperimentId = id,
            Name = $"M3 benchmark fixture {id}",
            GridWidth = gridSize,
            GridHeight = gridSize,
            BoundaryMode = boundaryMode,
            McsCount = mcsCount,
            Initialiser = baseline.Initialiser with { CellCount = cellCount },
            Measurements = measurements,
            ReplicateCount = 1
        };
    }
}
