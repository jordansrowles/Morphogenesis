using BenchmarkDotNet.Attributes;

namespace Rowles.Morphogenesis.Benchmarks;

/// <summary>Eight-neighbour reads plus local accepted writes, including periodic _halo maintenance.</summary>
[MemoryDiagnoser]
[InvocationCount(1)]
public class LatticeLayoutBenchmarks
{
    private int[] _straight = null!;
    private int[] _halo = null!;
    private int[] _targets = null!;
    private int[] _offsets = null!;
    private int _stride;

    [Params(32, 128, 256)]
    public int Width { get; set; }

    [Params(false, true)]
    public bool Periodic { get; set; }

    [GlobalSetup]
    public void SetUp()
    {
        _straight = new int[Width * Width];
        _stride = Width + 2;
        _halo = new int[_stride * _stride];
        _offsets = [-_stride - 1, -_stride, -_stride + 1, -1, 1, _stride - 1, _stride, _stride + 1];
        _targets = new int[65536];
        System.Random random = new(1701);
        for (int i = 0; i < _targets.Length; i++) _targets[i] = random.Next(_straight.Length);
        Reset();
        long expected = StraightFlat();
        int[] finalStraight = (int[])_straight.Clone();
        Reset();
        if (expected != PaddedHalo()) throw new InvalidOperationException("Lattice layout read sequence differs.");
        for (int y = 0; y < Width; y++)
            for (int x = 0; x < Width; x++)
                if (_halo[(y + 1) * _stride + x + 1] != finalStraight[y * Width + x])
                    throw new InvalidOperationException("Lattice layout writes differ.");
        // Check all refreshed edge and corner copies, independently of proposal coverage.
        for (int y = -1; y <= Width; y++)
            for (int x = -1; x <= Width; x++)
            {
                int value = (x < 0 || y < 0 || x >= Width || y >= Width) && !Periodic
                    ? -1 : finalStraight[((y + Width) % Width) * Width + (x + Width) % Width];
                if (_halo[(y + 1) * _stride + x + 1] != value)
                    throw new InvalidOperationException("Halo maintenance differs.");
            }
    }

    [IterationSetup]
    public void Reset()
    {
        for (int i = 0; i < _straight.Length; i++) _straight[i] = i % 127;
        for (int y = -1; y <= Width; y++)
            for (int x = -1; x <= Width; x++)
                _halo[(y + 1) * _stride + x + 1] =
                    (x < 0 || y < 0 || x >= Width || y >= Width) && !Periodic ? -1 :
                    _straight[((y + Width) % Width) * Width + (x + Width) % Width];
    }

    [Benchmark(Baseline = true)]
    public long StraightFlat()
    {
        long sum = 0;
        for (int pass = 0; pass < 128; pass++)
            for (int i = 0; i < _targets.Length; i++)
            {
                int target = _targets[i];
                int x = target % Width, y = target / Width;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (Periodic)
                        {
                            if (nx < 0) nx += Width; else if (nx >= Width) nx -= Width;
                            if (ny < 0) ny += Width; else if (ny >= Width) ny -= Width;
                            sum += _straight[ny * Width + nx];
                        }
                        else sum += (uint)nx >= (uint)Width || (uint)ny >= (uint)Width
                            ? -1 : _straight[ny * Width + nx];
                    }
                if ((i & 15) == 0) _straight[target] = (i / 16) % 127;
            }
        return sum;
    }

    [Benchmark]
    public long PaddedHalo()
    {
        long sum = 0;
        for (int pass = 0; pass < 128; pass++)
            for (int i = 0; i < _targets.Length; i++)
            {
                int target = _targets[i];
                int x = target % Width, y = target / Width;
                int index = (y + 1) * _stride + x + 1;
                for (int offset = 0; offset < _offsets.Length; offset++) sum += _halo[index + _offsets[offset]];
                if ((i & 15) != 0) continue;
                int value = (i / 16) % 127;
                _halo[index] = value;
                if (!Periodic) continue;
                // A corner mutation refreshes three _halo copies; an edge mutation refreshes one.
                int copyX = x == 0 ? Width + 1 : x == Width - 1 ? 0 : -1;
                int copyY = y == 0 ? Width + 1 : y == Width - 1 ? 0 : -1;
                if (copyX >= 0) _halo[(y + 1) * _stride + copyX] = value;
                if (copyY >= 0) _halo[copyY * _stride + x + 1] = value;
                if (copyX >= 0 && copyY >= 0) _halo[copyY * _stride + copyX] = value;
            }
        return sum;
    }
}
