namespace Rowles.Morphogenesis.Reference.Random;

/// <summary>
/// xoshiro256** with a four-word state initialised by SplitMix64.
/// This generator is a deterministic simulation contract, not a cryptographic source.
/// </summary>
public static class BoundedIntegerSampling
{
    public static ulong Sample(ulong exclusiveUpperBound, Func<ulong> nextUInt64)
    {
        ArgumentNullException.ThrowIfNull(nextUInt64);
        if (exclusiveUpperBound == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
        }

        ulong rejectionThreshold = unchecked(0UL - exclusiveUpperBound) % exclusiveUpperBound;
        while (true)
        {
            ulong value = nextUInt64();
            if (value >= rejectionThreshold)
            {
                return value % exclusiveUpperBound;
            }
        }
    }
}
