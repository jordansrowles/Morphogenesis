using System.Globalization;
using Xunit;

namespace Rowles.StrictMaths.Tests;

public sealed class CrossPlatformGoldenTests
{
    // Reference columns use offline 700-digit decimal evaluation with Machin pi and Taylor reduction for sin/cos.
    // They are checked in so Linux and Windows run the same inputs and expectations without runtime maths dependencies.
    [Theory]
    [MemberData(nameof(ExpVectors))]
    public void ExpMatchesFixedBinary64Vectors(string inputBits, string expectedBits)
    {
        double actual = StrictMath.Exp(ToDouble(inputBits));
        Assert.Equal(expectedBits, ToBits(actual));
    }

    [Theory]
    [MemberData(nameof(LogVectors))]
    public void LogMatchesFixedBinary64Vectors(string inputBits, string expectedBits)
    {
        double actual = StrictMath.Log(ToDouble(inputBits));
        Assert.Equal(expectedBits, ToBits(actual));
    }

    [Theory]
    [MemberData(nameof(SqrtVectors))]
    public void SqrtMatchesFixedBinary64Vectors(string inputBits, string expectedBits)
    {
        double actual = StrictMath.Sqrt(ToDouble(inputBits));
        Assert.Equal(expectedBits, ToBits(actual));
    }

    [Theory]
    [MemberData(nameof(SinVectors))]
    public void SinMatchesFixedBinary64Vectors(string inputBits, string expectedBits)
    {
        double actual = StrictMath.Sin(ToDouble(inputBits));
        Assert.Equal(expectedBits, ToBits(actual));
    }

    [Theory]
    [MemberData(nameof(CosVectors))]
    public void CosMatchesFixedBinary64Vectors(string inputBits, string expectedBits)
    {
        double actual = StrictMath.Cos(ToDouble(inputBits));
        Assert.Equal(expectedBits, ToBits(actual));
    }

    [Theory]
    [MemberData(nameof(ExpAccuracyVectors))]
    public void ExpStaysWithinOneUlpOfTheHighPrecisionReference(string inputBits, string expectedBits)
    {
        AssertWithinOneUlp(StrictMath.Exp(ToDouble(inputBits)), expectedBits);
    }

    [Theory]
    [MemberData(nameof(LogAccuracyVectors))]
    public void LogStaysWithinOneUlpOfTheHighPrecisionReference(string inputBits, string expectedBits)
    {
        AssertWithinOneUlp(StrictMath.Log(ToDouble(inputBits)), expectedBits);
    }

    [Theory]
    [MemberData(nameof(SqrtAccuracyVectors))]
    public void SqrtMatchesTheHighPrecisionReference(string inputBits, string expectedBits)
    {
        double actual = StrictMath.Sqrt(ToDouble(inputBits));
        if (expectedBits == "NaN")
        {
            Assert.True(double.IsNaN(actual));
            return;
        }

        Assert.Equal(expectedBits, ToBits(actual));
    }

    [Theory]
    [MemberData(nameof(SinAccuracyVectors))]
    public void SinStaysWithinOneUlpOfTheHighPrecisionReference(string inputBits, string expectedBits)
    {
        AssertWithinOneUlp(StrictMath.Sin(ToDouble(inputBits)), expectedBits);
    }

    [Theory]
    [MemberData(nameof(CosAccuracyVectors))]
    public void CosStaysWithinOneUlpOfTheHighPrecisionReference(string inputBits, string expectedBits)
    {
        AssertWithinOneUlp(StrictMath.Cos(ToDouble(inputBits)), expectedBits);
    }

    public static IEnumerable<object[]> ExpVectors => Rows().Select(row => new object[] { row[0], row[1] });
    public static IEnumerable<object[]> LogVectors => Rows().Select(row => new object[] { row[0], row[2] });
    public static IEnumerable<object[]> SqrtVectors => Rows().Select(row => new object[] { row[0], row[3] });
    public static IEnumerable<object[]> SinVectors => Rows().Select(row => new object[] { row[0], row[4] });
    public static IEnumerable<object[]> CosVectors => Rows().Select(row => new object[] { row[0], row[5] });
    public static IEnumerable<object[]> ExpAccuracyVectors => Rows().Select(row => new object[] { row[0], row[6] });
    public static IEnumerable<object[]> LogAccuracyVectors => Rows().Select(row => new object[] { row[0], row[7] });
    public static IEnumerable<object[]> SqrtAccuracyVectors => Rows().Select(row => new object[] { row[0], row[8] });
    public static IEnumerable<object[]> SinAccuracyVectors => Rows().Select(row => new object[] { row[0], row[9] });
    public static IEnumerable<object[]> CosAccuracyVectors => Rows().Select(row => new object[] { row[0], row[10] });

    private static string[][] Rows() => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "StrictMath.golden.tsv"))
        .Where(line => line.Length > 0 && line[0] != '#')
        .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .ToArray();

    private static void AssertWithinOneUlp(double actual, string referenceBits)
    {
        if (referenceBits == "NaN")
        {
            Assert.True(double.IsNaN(actual));
            return;
        }

        double reference = ToDouble(referenceBits);
        if (double.IsInfinity(reference))
        {
            Assert.Equal(reference, actual);
            return;
        }

        ulong actualOrdered = Ordered(BitConverter.DoubleToInt64Bits(actual));
        ulong expectedOrdered = Ordered(BitConverter.DoubleToInt64Bits(reference));
        ulong distance = actualOrdered >= expectedOrdered
            ? actualOrdered - expectedOrdered
            : expectedOrdered - actualOrdered;
        Assert.InRange(distance, 0UL, 1UL);
    }

    private static ulong Ordered(long bits) => bits < 0
        ? unchecked((ulong)~bits)
        : (ulong)bits | 0x8000000000000000UL;

    private static double ToDouble(string bits) =>
        BitConverter.Int64BitsToDouble(unchecked((long)ulong.Parse(bits, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));

    private static string ToBits(double value) =>
        unchecked((ulong)BitConverter.DoubleToInt64Bits(value)).ToString("X16", CultureInfo.InvariantCulture);
}
