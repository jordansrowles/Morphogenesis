using Rowles.Morphogenesis.Benchmarks.Analysis;

namespace Rowles.Morphogenesis.Tests.Analysis;

public sealed class PairedIntervalTests
{
    [Fact]
    public void Identical_samples_have_a_zero_interval()
    {
        double[] values = Enumerable.Range(0, 64).Select(value => (double)value).ToArray();
        PairedIntervalResult result = PairedInterval.Calculate(values, values, 0);
        Assert.Equal(0, result.MeanDifference);
        Assert.Equal(0, result.SampleStandardDeviation);
        Assert.Equal(0, result.LowerBound);
        Assert.Equal(0, result.UpperBound);
        Assert.True(result.IsEquivalent);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(-0.25)]
    public void Fixed_offsets_produce_ordered_zero_width_intervals(double offset)
    {
        double[] canonical = Enumerable.Range(0, 64).Select(value => (double)value).ToArray();
        double[] candidate = canonical.Select(value => value + offset).ToArray();
        PairedIntervalResult result = PairedInterval.Calculate(canonical, candidate, Math.Abs(offset));
        Assert.Equal(offset, result.MeanDifference, 10);
        Assert.Equal(result.MeanDifference, result.LowerBound);
        Assert.Equal(result.MeanDifference, result.UpperBound);
        Assert.True(result.IsEquivalent);
    }

    [Fact]
    public void Fixed_dataset_uses_the_paired_sample_standard_deviation_and_student_interval()
    {
        double[] canonical = Enumerable.Range(0, 64).Select(value => (double)value).ToArray();
        double[] candidate = canonical.Select((value, index) => value + (index < 32 ? -1 : 1)).ToArray();
        PairedIntervalResult result = PairedInterval.Calculate(canonical, candidate, 1);
        Assert.Equal(0, result.MeanDifference, 12);
        Assert.Equal(Math.Sqrt(64d / 63), result.SampleStandardDeviation, 12);
        Assert.InRange(result.LowerBound, -0.211, -0.210);
        Assert.InRange(result.UpperBound, 0.210, 0.211);
        Assert.True(result.LowerBound < result.UpperBound);
        Assert.True(result.IsEquivalent);
    }

    [Fact]
    public void Confidence_interval_must_fit_entirely_inside_the_band()
    {
        double[] canonical = Enumerable.Range(0, 64).Select(value => (double)value).ToArray();
        double[] candidate = canonical.Select((value, index) => value + (index < 32 ? -1 : 1)).ToArray();
        PairedIntervalResult measured = PairedInterval.Calculate(canonical, candidate, double.MaxValue);
        double exactMargin = Math.Max(Math.Abs(measured.LowerBound), Math.Abs(measured.UpperBound));
        Assert.True(PairedInterval.Calculate(canonical, candidate, exactMargin).IsEquivalent);
        Assert.False(PairedInterval.Calculate(canonical, candidate, Math.BitDecrement(exactMargin)).IsEquivalent);
        Assert.False(PairedInterval.Calculate(canonical, candidate, 0).IsEquivalent);
    }

    [Fact]
    public void Zero_canonical_variability_keeps_a_zero_width_band()
    {
        double[] canonical = Enumerable.Repeat(4d, 64).ToArray();
        Assert.True(PairedInterval.Calculate(canonical, canonical, 0).IsEquivalent);
        Assert.False(PairedInterval.Calculate(canonical, canonical.Select(value => value + 0.01).ToArray(), 0).IsEquivalent);
    }
}
