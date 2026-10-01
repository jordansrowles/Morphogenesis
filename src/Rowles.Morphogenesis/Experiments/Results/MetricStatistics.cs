namespace Rowles.Morphogenesis.Experiments.Results;

/// <summary>Statistics over successful final measurements; failed replicates are counted separately.</summary>
public sealed record MetricStatistics(
    int Count,
    double Mean,
    double Median,
    double SampleStandardDeviation,
    double Minimum,
    double Maximum)
{
    public static MetricStatistics From(IEnumerable<double> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        double[] values = source.Order().ToArray();
        if (values.Any(value => !double.IsFinite(value)))
        {
            throw new ArgumentException("Metric statistics require finite values.", nameof(source));
        }

        if (values.Length == 0)
        {
            return new MetricStatistics(0, 0, 0, 0, 0, 0);
        }

        double mean = values.Average();
        double median = values.Length % 2 == 0
            ? (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2
            : values[values.Length / 2];
        double variance = values.Length < 2
            ? 0
            : values.Sum(value => (value - mean) * (value - mean)) / (values.Length - 1);
        return new MetricStatistics(values.Length, mean, median, Math.Sqrt(variance), values[0], values[^1]);
    }
}
