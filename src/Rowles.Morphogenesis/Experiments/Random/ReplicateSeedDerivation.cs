namespace Rowles.Morphogenesis.Experiments.Random;

/// <summary>Derives a replicate seed by mixing the base seed plus the SplitMix64 golden-ratio step times index plus one.</summary>
public static class ReplicateSeedDerivation
{
    public static ulong Derive(ulong baseSeed, int replicateIndex)
    {
        if (replicateIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(replicateIndex));
        }

        ulong input = unchecked(baseSeed + 0x9E3779B97F4A7C15UL * ((ulong)replicateIndex + 1));
        return Mix(input);
    }

    public static ulong[] DeriveRange(ulong baseSeed, int replicateCount)
    {
        if (replicateCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(replicateCount));
        }

        ulong[] seeds = new ulong[replicateCount];
        HashSet<ulong> unique = [];
        for (int index = 0; index < seeds.Length; index++)
        {
            ulong seed = Derive(baseSeed, index);
            if (!unique.Add(seed))
            {
                throw new InvalidOperationException("Replicate seed derivation produced a duplicate seed.");
            }

            seeds[index] = seed;
        }

        return seeds;
    }

    internal static ulong Mix(ulong value)
    {
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
