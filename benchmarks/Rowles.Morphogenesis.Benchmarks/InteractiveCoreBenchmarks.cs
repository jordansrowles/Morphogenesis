using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Random;

namespace Rowles.Morphogenesis.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class InteractiveStateReadoutBenchmarks
{
    [Params(32, 64, 128, 256, 512)]
    public int Width { get; set; }

    private MorphogenesisState _state = null!;
    private int[] _destination = null!;

    [GlobalSetup]
    public void Setup()
    {
        int side = Width / 4;
        int start = (Width - side) / 2;
        int[] ids = new int[Width * Width];
        for (int y = start; y < start + side; y++)
        {
            for (int x = start; x < start + side; x++)
            {
                ids[y * Width + x] = 1;
            }
        }

        _state = new MorphogenesisState(
            Width,
            Width,
            ids,
            [new CellDefinition(1, 1, side * side, 0.5, 0, 0)],
            new ContactEnergyMatrix(new double[,] { { 0, 2 }, { 2, 0 } }),
            SimulationConfiguration.WallCanonical);
        _destination = new int[_state.SiteCount];
    }

    [Benchmark]
    public int CopyCellIdsTo()
    {
        _state.CopyCellIdsTo(_destination);
        return _destination[Width * Width / 2];
    }

    [Benchmark]
    public int EnumerateLiveCellMetadata()
    {
        int totalArea = 0;
        for (int cellId = 1; cellId < _state.CellCapacity; cellId++)
        {
            if (_state.TryGetCellState(cellId, out CellState cell))
            {
                totalArea += cell.Area;
            }
        }

        return totalArea;
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class InteractiveMutationSinkBenchmarks
{
    private static readonly double[,] Contacts = { { 0, 4 }, { 4, 0 } };
    private SerialSimulation _withoutSink = null!;
    private SerialSimulation _withNoOpSink = null!;
    private SerialSimulation _withCountingSink = null!;
    private CountingSink _countingSink = null!;

    [IterationSetup]
    public void ResetSimulations()
    {
        _withoutSink = CreateSimulation(null);
        _withNoOpSink = CreateSimulation(NoOpSink.Instance);
        _countingSink = new CountingSink();
        _withCountingSink = CreateSimulation(_countingSink);
    }

    [Benchmark]
    public McsSummary RunMcsWithoutSink() => _withoutSink.RunMcs();

    [Benchmark]
    public McsSummary RunMcsWithNoOpSink() => _withNoOpSink.RunMcs();

    [Benchmark]
    public (McsSummary Summary, long AcceptedCopies) RunMcsWithCountingSink()
    {
        McsSummary summary = _withCountingSink.RunMcs();
        return (summary, _countingSink.AcceptedCopies);
    }

    private static SerialSimulation CreateSimulation(ILatticeMutationSink? mutationSink)
    {
        const int Width = 64;
        const int Side = 16;
        int start = (Width - Side) / 2;
        int[] ids = new int[Width * Width];
        for (int y = start; y < start + Side; y++)
        {
            for (int x = start; x < start + Side; x++)
            {
                ids[y * Width + x] = 1;
            }
        }

        MorphogenesisState state = new(
            Width,
            Width,
            ids,
            [new CellDefinition(1, 1, Side * Side, 0.5, 64, 0.1)],
            new ContactEnergyMatrix(Contacts),
            SimulationConfiguration.WallCanonical);
        return new SerialSimulation(state, new Xoshiro256StarStar(0xC0FFEEUL), 8, mutationSink);
    }

    private sealed class NoOpSink : ILatticeMutationSink
    {
        internal static NoOpSink Instance { get; } = new();

        public void AcceptedCopy(int targetIndex, int oldCellId, int newCellId)
        {
        }
    }

    private sealed class CountingSink : ILatticeMutationSink
    {
        internal long AcceptedCopies { get; private set; }

        public void AcceptedCopy(int targetIndex, int oldCellId, int newCellId) => AcceptedCopies++;
    }
}
