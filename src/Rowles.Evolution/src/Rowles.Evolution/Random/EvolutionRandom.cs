namespace Rowles.Evolution.Random;

/// <summary>xoshiro256** with owned state and a checkpointable Box-Muller spare.</summary>
public sealed class EvolutionRandom
{
    private ulong s0, s1, s2, s3;
    private bool hasGaussian;
    private double gaussian;

    public EvolutionRandom(ulong seed)
    {
        ulong state = seed;
        s0 = SplitMix(ref state); s1 = SplitMix(ref state); s2 = SplitMix(ref state); s3 = SplitMix(ref state);
        if ((s0 | s1 | s2 | s3) == 0) s0 = 1;
    }

    internal EvolutionRandom(RandomState state)
    {
        s0 = state.S0; s1 = state.S1; s2 = state.S2; s3 = state.S3;
        hasGaussian = state.HasGaussian;
        gaussian = state.Gaussian;
        if ((s0 | s1 | s2 | s3) == 0 || (hasGaussian && !double.IsFinite(gaussian)))
            throw new ArgumentException("Random checkpoint state is invalid.", nameof(state));
    }

    public ulong NextUInt64()
    {
        ulong result = RotateLeft(s1 * 5, 7) * 9;
        ulong t = s1 << 17;
        s2 ^= s0; s3 ^= s1; s1 ^= s2; s0 ^= s3; s2 ^= t; s3 = RotateLeft(s3, 45);
        return result;
    }

    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    public int NextInt32(int exclusiveUpperBound)
    {
        if (exclusiveUpperBound <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
        ulong bound = (uint)exclusiveUpperBound;
        ulong threshold = unchecked(0UL - bound) % bound;
        while (true)
        {
            ulong value = NextUInt64();
            if (value >= threshold) return (int)(value % bound);
        }
    }

    public double NextGaussian()
    {
        if (hasGaussian)
        {
            hasGaussian = false;
            return gaussian;
        }

        double u1;
        do { u1 = NextDouble(); } while (u1 <= 0);
        double u2 = NextDouble();
        double radius = Math.Sqrt(-2 * Math.Log(u1));
        double angle = 2 * Math.PI * u2;
        gaussian = radius * Math.Sin(angle);
        hasGaussian = true;
        return radius * Math.Cos(angle);
    }

    internal RandomState Capture() => new(s0, s1, s2, s3, hasGaussian, gaussian);
    private static ulong RotateLeft(ulong value, int count) => (value << count) | (value >> (64 - count));
    private static ulong SplitMix(ref ulong value)
    {
        ulong z = (value += 0x9E3779B97F4A7C15UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
