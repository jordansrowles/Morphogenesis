using BenchmarkDotNet.Attributes;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Benchmarks;

/// <summary>Identical proposal-local loads and accepted geometry writes, without lattice work.</summary>
[MemoryDiagnoser]
[InvocationCount(1)]
public class CellLayoutBenchmarks
{
    private CellRuntime[] _cells = null!;
    private int[] _areas = null!;
    private int[] _perimeters = null!;
    private int[] _types = null!;
    private double[] _targetAreas = null!;
    private double[] _targetPerimeters = null!;
    private double[] _areaStiffnesses = null!;
    private double[] _perimeterStiffnesses = null!;
    private readonly double[] _typeAreaStiffnesses = [2, 3, 4];
    private readonly double[] _typePerimeterStiffnesses = [0.5, 1, 1.5];
    private int[] _proposals = null!;

    [Params(64, 1024, 4096)]
    public int CellCount { get; set; }

    [GlobalSetup]
    public void SetUp()
    {
        _cells = new CellRuntime[CellCount];
        _areas = new int[CellCount];
        _perimeters = new int[CellCount];
        _types = new int[CellCount];
        _targetAreas = new double[CellCount];
        _targetPerimeters = new double[CellCount];
        _areaStiffnesses = new double[CellCount];
        _perimeterStiffnesses = new double[CellCount];
        _proposals = new int[65536];
        System.Random random = new(1701);
        for (int i = 0; i < CellCount; i++)
        {
            int type = i % 3;
            _cells[i] = new CellRuntime
            {
                CellTypeId = type,
                IsAlive = true,
                Area = 12 + i % 9,
                Perimeter = 14 + i % 7,
                TargetArea = 16 + i % 2,
                TargetPerimeter = 16 + i % 3,
                AreaStiffness = _typeAreaStiffnesses[type],
                PerimeterStiffness = _typePerimeterStiffnesses[type]
            };
            _areas[i] = _cells[i].Area;
            _perimeters[i] = _cells[i].Perimeter;
            _types[i] = type;
            _targetAreas[i] = _cells[i].TargetArea;
            _targetPerimeters[i] = _cells[i].TargetPerimeter;
            _areaStiffnesses[i] = _cells[i].AreaStiffness;
            _perimeterStiffnesses[i] = _cells[i].PerimeterStiffness;
        }
        for (int i = 0; i < _proposals.Length; i++) _proposals[i] = random.Next(CellCount);
        double baseline = ArrayOfStructures();
        Reset();
        double soa = StructureOfArrays();
        Reset();
        double shared = SharedTypeTables();
        if (BitConverter.DoubleToInt64Bits(baseline) != BitConverter.DoubleToInt64Bits(soa) ||
            BitConverter.DoubleToInt64Bits(baseline) != BitConverter.DoubleToInt64Bits(shared))
            throw new InvalidOperationException("Cell layout arithmetic differs.");
    }

    [IterationSetup]
    public void Reset()
    {
        for (int i = 0; i < CellCount; i++)
        {
            _cells[i].Area = _areas[i] = 12 + i % 9;
            _cells[i].Perimeter = _perimeters[i] = 14 + i % 7;
        }
    }

    [Benchmark(Baseline = true)]
    public double ArrayOfStructures()
    {
        double sum = 0;
        for (int pass = 0; pass < 512; pass++)
            for (int i = 0; i < _proposals.Length; i++)
            {
                ref CellRuntime cell = ref _cells[_proposals[i]];
                sum += Delta(cell.Area, cell.Perimeter, cell.TargetArea, cell.TargetPerimeter,
                    cell.AreaStiffness, cell.PerimeterStiffness);
                if ((i & 15) == 0) { cell.Area++; cell.Perimeter += 2; }
            }
        return sum;
    }

    [Benchmark]
    public double StructureOfArrays()
    {
        double sum = 0;
        for (int pass = 0; pass < 512; pass++)
            for (int i = 0; i < _proposals.Length; i++)
            {
                int id = _proposals[i];
                sum += Delta(_areas[id], _perimeters[id], _targetAreas[id], _targetPerimeters[id],
                    _areaStiffnesses[id], _perimeterStiffnesses[id]);
                if ((i & 15) == 0) { _areas[id]++; _perimeters[id] += 2; }
            }
        return sum;
    }

    [Benchmark]
    public double SharedTypeTables()
    {
        double sum = 0;
        for (int pass = 0; pass < 512; pass++)
            for (int i = 0; i < _proposals.Length; i++)
            {
                int id = _proposals[i];
                int type = _types[id];
                sum += Delta(_areas[id], _perimeters[id], _targetAreas[id], _targetPerimeters[id],
                    _typeAreaStiffnesses[type], _typePerimeterStiffnesses[type]);
                if ((i & 15) == 0) { _areas[id]++; _perimeters[id] += 2; }
            }
        return sum;
    }

    private static double Delta(int area, int perimeter, double targetArea, double targetPerimeter,
        double areaStiffness, double perimeterStiffness)
    {
        double areaBefore = area - targetArea;
        double areaAfter = area + 1 - targetArea;
        double perimeterBefore = perimeter - targetPerimeter;
        double perimeterAfter = perimeter + 2 - targetPerimeter;
        return areaStiffness * (areaAfter * areaAfter - areaBefore * areaBefore) +
            perimeterStiffness * (perimeterAfter * perimeterAfter - perimeterBefore * perimeterBefore);
    }
}
