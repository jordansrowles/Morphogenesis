using Rowles.Morphogenesis.Reference.Random;

namespace Rowles.Morphogenesis.Reference.Tests.Random;

public sealed class XoshiroTests
{
    [Fact]
    public void Seed_zero_has_frozen_raw_xoshiro256_star_star_outputs()
    {
        Xoshiro256StarStar random = new(0);
        ulong[] expected =
        [
            0x99EC5F36CB75F2B4UL,
            0xBF6E1F784956452AUL,
            0x1A5F849D4933E6E0UL,
            0x6AA594F1262D2D2CUL,
            0xBBA5AD4A1F842E59UL,
            0xFFEF8375D9EBCACAUL,
            0x6C160DEED2F54C98UL,
            0x8920AD648FC30A3FUL
        ];

        Assert.Equal(expected, Enumerable.Range(0, expected.Length).Select(_ => random.NextUInt64()));
    }

    [Fact]
    public void Seed_one_has_a_second_frozen_raw_output_vector()
    {
        Xoshiro256StarStar random = new(1);

        Assert.Equal(0xB3F2AF6D0FC710C5UL, random.NextUInt64());
        Assert.Equal(0x853B559647364CEAUL, random.NextUInt64());
        Assert.Equal(0x92F89756082A4514UL, random.NextUInt64());
        Assert.Equal(0x642E1C7BC266A3A7UL, random.NextUInt64());
    }

    [Fact]
    public void Bounded_samples_have_frozen_known_answers()
    {
        Xoshiro256StarStar random = new(0);

        Assert.Equal([5, 8, 2, 16, 13, 0, 10, 3], Enumerable.Range(0, 8).Select(_ => random.NextInt(17)));
    }

    [Fact]
    public void Bounded_sampling_rejects_the_biased_low_tail()
    {
        ulong[] values = [4, 27];
        int index = 0;

        ulong result = BoundedIntegerSampling.Sample(10, () => values[index++]);

        Assert.Equal(7UL, result);
        Assert.Equal(2, index);
    }

    [Fact]
    public void Bounded_sampling_handles_a_unit_range()
    {
        Assert.Equal(0UL, BoundedIntegerSampling.Sample(1, () => ulong.MaxValue));
    }

    [Fact]
    public void Uniform_double_conversion_has_frozen_known_answers()
    {
        Xoshiro256StarStar random = new(0);
        double[] expected = [0.6012629994179048, 0.7477740925472398, 0.10301998939503632, 0.4165890778296456];

        Assert.Equal(expected, Enumerable.Range(0, expected.Length).Select(_ => random.NextDouble()));
        Assert.All(expected, value => Assert.InRange(value, 0, Math.BitDecrement(1)));
    }

    [Fact]
    public void Bounded_sampling_rejects_zero_bound()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BoundedIntegerSampling.Sample(0, () => 1));
    }

    [Fact]
    public void Integer_sampling_rejects_non_positive_bound()
    {
        Xoshiro256StarStar random = new(0);

        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(-1));
    }
}
