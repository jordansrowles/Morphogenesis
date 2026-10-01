namespace Rowles.Morphogenesis.Reference.Random;

/// <summary>
/// xoshiro256** with a four-word state initialised by SplitMix64.
/// This generator is a deterministic simulation contract, not a cryptographic source.
/// </summary>
public sealed class Xoshiro256StarStar : IRandomSource
{
    private ulong _state0;
    private ulong _state1;
    private ulong _state2;
    private ulong _state3;

    public Xoshiro256StarStar(ulong seed)
    {
        SplitMix64 splitMix = new(seed);
        _state0 = splitMix.NextUInt64();
        _state1 = splitMix.NextUInt64();
        _state2 = splitMix.NextUInt64();
        _state3 = splitMix.NextUInt64();
    }

    public ulong NextUInt64()
    {
        ulong result = RotateLeft(_state1 * 5, 7) * 9;
        ulong temporary = _state1 << 17;

        _state2 ^= _state0;
        _state3 ^= _state1;
        _state1 ^= _state2;
        _state0 ^= _state3;
        _state2 ^= temporary;
        _state3 = RotateLeft(_state3, 45);

        return result;
    }

    public int NextInt(int exclusiveUpperBound)
    {
        if (exclusiveUpperBound <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
        }

        ulong sampled = BoundedIntegerSampling.Sample((ulong)exclusiveUpperBound, NextUInt64);
        return checked((int)sampled);
    }

    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    private static ulong RotateLeft(ulong value, int shift) => (value << shift) | (value >> (64 - shift));

    private struct SplitMix64(ulong seed)
    {
        private ulong _state = seed;

        public ulong NextUInt64()
        {
            ulong value = unchecked(_state += 0x9E3779B97F4A7C15UL);
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
