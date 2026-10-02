namespace Rowles.Morphogenesis.Benchmarks.Analysis;

internal readonly record struct PairedIntervalResult(
    double MeanDifference,
    double SampleStandardDeviation,
    double LowerBound,
    double UpperBound,
    bool IsEquivalent);

internal static class PairedInterval
{
    internal static PairedIntervalResult Calculate(
        IReadOnlyList<double> canonical,
        IReadOnlyList<double> candidate,
        double practicalHalfWidth)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(candidate);
        if (canonical.Count != candidate.Count || canonical.Count < 2)
        {
            throw new ArgumentException("Paired samples must have the same count of at least two.");
        }
        if (!double.IsFinite(practicalHalfWidth) || practicalHalfWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(practicalHalfWidth));
        }

        double mean = 0;
        double sumSquares = 0;
        int count = 0;
        for (int index = 0; index < canonical.Count; index++)
        {
            double difference = candidate[index] - canonical[index];
            if (!double.IsFinite(difference))
            {
                throw new ArgumentException("Paired samples must contain only finite values.");
            }

            count++;
            double delta = difference - mean;
            mean += delta / count;
            sumSquares += delta * (difference - mean);
        }

        double standardDeviation = Math.Sqrt(sumSquares / (count - 1));
        double margin = EventEnsembleProtocol.StudentCriticalValue(count) * standardDeviation / Math.Sqrt(count);
        double lower = mean - margin;
        double upper = mean + margin;
        return new PairedIntervalResult(mean, standardDeviation, lower, upper,
            lower >= -practicalHalfWidth && upper <= practicalHalfWidth);
    }

    internal static double Mean(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0) throw new ArgumentException("At least one value is required.", nameof(values));
        double mean = 0;
        for (int index = 0; index < values.Count; index++)
        {
            double value = values[index];
            if (!double.IsFinite(value)) throw new ArgumentException("Values must be finite.", nameof(values));
            mean += (value - mean) / (index + 1);
        }
        return mean;
    }

    internal static double SampleStandardDeviation(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 2) throw new ArgumentException("At least two values are required.", nameof(values));
        double mean = Mean(values);
        double sumSquares = 0;
        foreach (double value in values)
        {
            double difference = value - mean;
            sumSquares += difference * difference;
        }
        return Math.Sqrt(sumSquares / (values.Count - 1));
    }
}
