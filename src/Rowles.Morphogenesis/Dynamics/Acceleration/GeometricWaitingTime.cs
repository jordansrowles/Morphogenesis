using Rowles.Morphogenesis.Random;
using Rowles.StrictMaths;

namespace Rowles.Morphogenesis.Dynamics.Acceleration;

/// <summary>Inverse-CDF geometric waiting time on the random source's binary64 grid.</summary>
internal static class GeometricWaitingTime
{
    internal static long Sample(int members, long proposalCount, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (proposalCount <= 0 || members < 0 || members > proposalCount)
            throw new ArgumentOutOfRangeException(nameof(members));
        if (members == 0) return long.MaxValue;
        if (members == proposalCount) return 0;
        double value = random.NextDouble();
        if (!double.IsFinite(value) || value < 0 || value >= 1)
            throw new InvalidOperationException("The waiting-time random value must be in [0, 1).");
        double probability = (double)members / proposalCount;
        // Both arguments are positive; StrictMath keeps logarithms platform-independent.
        // 1-value is exactly representable on the xoshiro 53-bit grid.
        double skipped = StrictMath.Log(1 - value) / StrictMath.Log(1 - probability);
        if (skipped >= long.MaxValue) return long.MaxValue;
        return (long)skipped;
    }
}
