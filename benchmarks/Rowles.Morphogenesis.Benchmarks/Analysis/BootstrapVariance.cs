namespace Rowles.Morphogenesis.Benchmarks.Analysis;

internal readonly record struct BootstrapVarianceResult(
    double Ratio,
    double LowerBound,
    double UpperBound,
    int ReplicateCount,
    bool IsEquivalent);

internal static class BootstrapVariance
{
    internal static BootstrapVarianceResult Calculate(
        IReadOnlyList<double> canonical,
        IReadOnlyList<double> candidate,
        string key)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(key);
        if (canonical.Count != candidate.Count || canonical.Count < 2)
        {
            throw new ArgumentException("Paired samples must have the same count of at least two.");
        }

        double canonicalVariance = Variance(canonical);
        double candidateVariance = Variance(candidate);
        double ratio = canonicalVariance == 0
            ? candidateVariance == 0 ? 1 : double.PositiveInfinity
            : candidateVariance / canonicalVariance;
        BootstrapRandom random = new(EventEnsembleProtocol.BootstrapSeed(key));
        int[] indexes = new int[canonical.Count];
        double[] ratios = new double[EventEnsembleProtocol.BootstrapReplicates];
        double[] sampledCanonical = new double[canonical.Count];
        double[] sampledCandidate = new double[candidate.Count];

        for (int replicate = 0; replicate < ratios.Length; replicate++)
        {
            for (int index = 0; index < indexes.Length; index++) indexes[index] = random.NextInt(indexes.Length);
            for (int index = 0; index < indexes.Length; index++)
            {
                sampledCanonical[index] = canonical[indexes[index]];
                sampledCandidate[index] = candidate[indexes[index]];
            }
            double resampledCanonicalVariance = Variance(sampledCanonical);
            double resampledCandidateVariance = Variance(sampledCandidate);
            ratios[replicate] = resampledCanonicalVariance == 0
                ? resampledCandidateVariance == 0 ? 1 : double.PositiveInfinity
                : resampledCandidateVariance / resampledCanonicalVariance;
        }

        Array.Sort(ratios);
        double lower = Quantile(ratios, 0.05);
        double upper = Quantile(ratios, 0.95);
        bool equivalent = lower >= EventEnsembleProtocol.VarianceRatioLowerBound &&
                          upper <= EventEnsembleProtocol.VarianceRatioUpperBound;
        return new BootstrapVarianceResult(ratio, lower, upper, ratios.Length, equivalent);
    }

    private static double Variance(IReadOnlyList<double> values)
    {
        double standardDeviation = PairedInterval.SampleStandardDeviation(values);
        return standardDeviation * standardDeviation;
    }

    private static double Quantile(double[] sorted, double fraction)
    {
        double position = (sorted.Length - 1) * fraction;
        int lower = (int)position;
        int upper = Math.Min(lower + 1, sorted.Length - 1);
        if (sorted[lower] == sorted[upper]) return sorted[lower];
        if (double.IsPositiveInfinity(sorted[upper])) return double.PositiveInfinity;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    // Preserve the accepted MT19937 integer-seeding and rejection-sampling sequence.
    private sealed class BootstrapRandom
    {
        private const int StateLength = 624;
        private readonly uint[] _state = new uint[StateLength];
        private int _index = StateLength;

        internal BootstrapRandom(ulong seed)
        {
            uint[] key = [(uint)seed, (uint)(seed >> 32)];
            InitialiseByArray(key);
        }

        internal int NextInt(int exclusiveUpperBound)
        {
            if (exclusiveUpperBound <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
            int bitCount = 32 - int.LeadingZeroCount(exclusiveUpperBound);
            uint value;
            do
            {
                value = NextUInt32() >> (32 - bitCount);
            }
            while (value >= exclusiveUpperBound);
            return (int)value;
        }

        private void InitialiseByArray(uint[] key)
        {
            Initialise(19650218U);
            int stateIndex = 1;
            int keyIndex = 0;
            int remaining = Math.Max(StateLength, key.Length);
            for (; remaining > 0; remaining--)
            {
                _state[stateIndex] = (_state[stateIndex] ^ ((_state[stateIndex - 1] ^ (_state[stateIndex - 1] >> 30)) * 1664525U)) + key[keyIndex] + (uint)keyIndex;
                stateIndex++;
                keyIndex++;
                if (stateIndex >= StateLength)
                {
                    _state[0] = _state[StateLength - 1];
                    stateIndex = 1;
                }
                if (keyIndex >= key.Length) keyIndex = 0;
            }
            for (remaining = StateLength - 1; remaining > 0; remaining--)
            {
                _state[stateIndex] = (_state[stateIndex] ^ ((_state[stateIndex - 1] ^ (_state[stateIndex - 1] >> 30)) * 1566083941U)) - (uint)stateIndex;
                stateIndex++;
                if (stateIndex >= StateLength)
                {
                    _state[0] = _state[StateLength - 1];
                    stateIndex = 1;
                }
            }
            _state[0] = 0x80000000U;
            _index = StateLength;
        }

        private void Initialise(uint seed)
        {
            _state[0] = seed;
            for (_index = 1; _index < StateLength; _index++)
                _state[_index] = 1812433253U * (_state[_index - 1] ^ (_state[_index - 1] >> 30)) + (uint)_index;
        }

        private uint NextUInt32()
        {
            if (_index >= StateLength)
            {
                for (int index = 0; index < StateLength; index++)
                {
                    uint joined = (_state[index] & 0x80000000U) | (_state[(index + 1) % StateLength] & 0x7fffffffU);
                    _state[index] = _state[(index + 397) % StateLength] ^ (joined >> 1) ^ ((joined & 1) == 0 ? 0U : 0x9908b0dfU);
                }
                _index = 0;
            }

            uint value = _state[_index++];
            value ^= value >> 11;
            value ^= (value << 7) & 0x9d2c5680U;
            value ^= (value << 15) & 0xefc60000U;
            value ^= value >> 18;
            return value;
        }
    }
}
