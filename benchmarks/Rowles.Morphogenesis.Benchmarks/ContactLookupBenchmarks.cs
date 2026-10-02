using BenchmarkDotNet.Attributes;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Benchmarks;

[MemoryDiagnoser]
public class ContactLookupBenchmarks
{
    private ContactEnergyMatrix _matrix = null!;
    private double[] _values = null!;
    private int[] _cellTypes = null!;
    private int[] _cellStrides = null!;
    private int[] _oldIds = null!;
    private int[] _newIds = null!;
    private int[] _neighbourIds = null!;

    [Params(3, 16)]
    public int TypeCount { get; set; }

    [GlobalSetup]
    public void SetUp()
    {
        double[,] source = new double[TypeCount, TypeCount];
        _values = new double[TypeCount * TypeCount];
        for (int a = 0; a < TypeCount; a++)
            for (int b = 0; b < TypeCount; b++)
                _values[a * TypeCount + b] = source[a, b] = (a + b) * 1.25;
        _matrix = new ContactEnergyMatrix(source);
        _cellTypes = new int[1024];
        _cellStrides = new int[1024];
        for (int id = 0; id < _cellTypes.Length; id++)
        {
            _cellTypes[id] = id % TypeCount;
            _cellStrides[id] = _cellTypes[id] * TypeCount;
        }
        System.Random random = new(1701);
        _oldIds = new int[65536]; _newIds = new int[65536]; _neighbourIds = new int[65536];
        for (int i = 0; i < _oldIds.Length; i++)
        {
            _oldIds[i] = random.Next(1024); _newIds[i] = random.Next(1024);
            _neighbourIds[i] = random.Next(1024);
        }
        long expected = BitConverter.DoubleToInt64Bits(CheckedIndexer());
        if (expected != BitConverter.DoubleToInt64Bits(PrevalidatedFlattened()) ||
            expected != BitConverter.DoubleToInt64Bits(PrecomputedCellStride()))
            throw new InvalidOperationException("Contact lookup arithmetic differs.");
    }

    [Benchmark(Baseline = true)]
    public double CheckedIndexer()
    {
        double delta = 0;
        for (int i = 0; i < _oldIds.Length; i++)
        {
            int old = _oldIds[i], next = _newIds[i], neighbour = _neighbourIds[i];
            int neighbourType = _cellTypes[neighbour];
            if (old != neighbour) delta -= _matrix[_cellTypes[old], neighbourType];
            if (next != neighbour) delta += _matrix[_cellTypes[next], neighbourType];
        }
        return delta;
    }

    [Benchmark]
    public double PrevalidatedFlattened()
    {
        double delta = 0;
        for (int i = 0; i < _oldIds.Length; i++)
        {
            int old = _oldIds[i], next = _newIds[i], neighbour = _neighbourIds[i];
            int neighbourType = _cellTypes[neighbour];
            if (old != neighbour) delta -= _values[_cellTypes[old] * TypeCount + neighbourType];
            if (next != neighbour) delta += _values[_cellTypes[next] * TypeCount + neighbourType];
        }
        return delta;
    }

    [Benchmark]
    public double PrecomputedCellStride()
    {
        double delta = 0;
        for (int i = 0; i < _oldIds.Length; i++)
        {
            int old = _oldIds[i], next = _newIds[i], neighbour = _neighbourIds[i];
            int neighbourType = _cellTypes[neighbour];
            if (old != neighbour) delta -= _values[_cellStrides[old] + neighbourType];
            if (next != neighbour) delta += _values[_cellStrides[next] + neighbourType];
        }
        return delta;
    }
}
