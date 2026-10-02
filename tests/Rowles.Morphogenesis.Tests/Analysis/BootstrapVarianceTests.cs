using Rowles.Morphogenesis.Benchmarks.Analysis;

namespace Rowles.Morphogenesis.Tests.Analysis;

public sealed class BootstrapVarianceTests
{
    [Fact]
    public void Equal_variance_samples_have_a_unit_ratio()
    {
        double[] values = Enumerable.Range(0, 64).Select(value => (double)value).ToArray();
        BootstrapVarianceResult result = BootstrapVariance.Calculate(values, values, "equal");
        Assert.Equal(1, result.Ratio);
        Assert.Equal(1, result.LowerBound);
        Assert.Equal(1, result.UpperBound);
        Assert.True(result.IsEquivalent);
    }

    [Fact]
    public void Candidate_variance_above_canonical_is_reported()
    {
        double[] canonical = Enumerable.Range(0, 64).Select(value => (double)value).ToArray();
        double mean = canonical.Average();
        double[] candidate = canonical.Select(value => mean + 3 * (value - mean)).ToArray();
        BootstrapVarianceResult result = BootstrapVariance.Calculate(canonical, candidate, "above");
        Assert.Equal(9, result.Ratio, 10);
        Assert.False(result.IsEquivalent);
    }

    [Fact]
    public void Candidate_variance_below_canonical_is_reported()
    {
        double[] canonical = Enumerable.Range(0, 64).Select(value => (double)value).ToArray();
        double mean = canonical.Average();
        double[] candidate = canonical.Select(value => mean + 0.1 * (value - mean)).ToArray();
        BootstrapVarianceResult result = BootstrapVariance.Calculate(canonical, candidate, "below");
        Assert.Equal(0.01, result.Ratio, 10);
        Assert.False(result.IsEquivalent);
    }

    [Fact]
    public void Bootstrap_is_repeatable_and_uses_exactly_512_paired_resamples()
    {
        double[] canonical = Enumerable.Range(0, 64).Select(value => (double)value).ToArray();
        double[] candidate = canonical.Select(value => value * value % 97).ToArray();
        BootstrapVarianceResult first = BootstrapVariance.Calculate(canonical, candidate, "condition");
        BootstrapVarianceResult second = BootstrapVariance.Calculate(canonical, candidate, "condition");
        Assert.Equal(EventEnsembleProtocol.BootstrapReplicates, first.ReplicateCount);
        Assert.Equal(first, second);
        Assert.Equal(2.7178335336538457, first.Ratio, 12);
        Assert.True(first.LowerBound <= first.UpperBound);
        Assert.Equal(2.159661203031037, first.LowerBound, 12);
        Assert.Equal(3.4864300216517417, first.UpperBound, 12);
    }

    [Fact]
    public void Zero_variance_handling_matches_the_protocol()
    {
        double[] constant = Enumerable.Repeat(2d, 64).ToArray();
        BootstrapVarianceResult equal = BootstrapVariance.Calculate(constant, constant, "zero-equal");
        Assert.Equal(1, equal.Ratio);
        Assert.Equal(1, equal.LowerBound);
        Assert.Equal(1, equal.UpperBound);
        Assert.Equal(0, equal.ReplicateCount);
        Assert.True(equal.IsEquivalent);
        double[] sparseOutlier = [.. Enumerable.Repeat(2d, 63), 3d];
        BootstrapVarianceResult different = BootstrapVariance.Calculate(constant, sparseOutlier, "zero-different");
        Assert.True(double.IsPositiveInfinity(different.Ratio));
        Assert.True(double.IsPositiveInfinity(different.LowerBound));
        Assert.True(double.IsPositiveInfinity(different.UpperBound));
        Assert.Equal(0, different.ReplicateCount);
        Assert.False(different.IsEquivalent);
    }
}
