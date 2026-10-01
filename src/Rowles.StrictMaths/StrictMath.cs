using System.Globalization;
using System.Numerics;

namespace Rowles.StrictMaths;

/// <summary>
/// Fixed binary64 elementary functions adapted from FDLIBM 5.3 algorithms.
/// The implementation uses round-to-nearest binary64 operations and exact
/// integer range reduction; it does not call host transcendental functions.
/// </summary>
/// <remarks>
/// The FDLIBM exp, log, sin and cos approximations target less than one ULP on
/// IEEE-754 binary64. Sqrt is correctly rounded to nearest, ties to even.
/// Managed expression evaluation is deliberately kept to ordinary double
/// operations; no fused multiply-add or platform maths intrinsic is used.
/// Trigonometric reduction uses 1400-bit fixed-point constants for 2/pi and
/// pi/2 so the same path covers every finite binary64 input.
/// </remarks>
public static class StrictMath
{
    private const int TrigScaleBits = 1400;
    private const ulong SignMask = 0x8000000000000000UL;
    private const ulong MagnitudeMask = 0x7fffffffffffffffUL;
    private const ulong FractionMask = 0x000fffffffffffffUL;

    private static readonly BigInteger TwoOverPi = ParseHex(
        "a2f9836e4e441529fc2757d1f534ddc0db6295993c439041fe5163abdebbc561b7246e3a424dd2e006492eea09d1921cfe1deb1cb129a73ee88235f52ebb4484e99c7026b45f7e413991d639835339f49c845f8bbdf9283b1ff897ffde05980fef2f118b5a0a6d1f6d367ecf27cb09b74f463f669e5fea2d7527bac7ebe5f17b3d0739f78a5292ea6bfb5fb11f8d5d0856033046fc7b6babf0cfbc209af4361da9e391615ee61b086599855f14a068");

    private static readonly BigInteger PiOverTwo = ParseHex(
        "1921fb54442d18469898cc51701b839a252049c1114cf98e804177d4c76273644a29410f31c6809bbdf2a33679a748636605614dbe4be286e9fc26adadaa3848bc90b6aecc4bcfd8de89885d34c6fdad617feb96de80d6fdbdc70d7f6b5133f4b5d3e4822f8963fcc9250cca3d9c8b67b8400f97142c77e0b31b4906c38aba734d22c7f51fa499ebf06caba47b9475b2c38c5e6ac410aa5773daa520ee12d2cdace186a9c95793009e2e8d811943042");

    private static readonly double Ln2High = FromBits(0x3fe62e42fee00000UL);
    private static readonly double Ln2Low = FromBits(0x3dea39ef35793c76UL);
    private static readonly double InverseLn2 = FromBits(0x3ff71547652b82feUL);
    private static readonly double InversePiOverTwo = FromBits(0x3fe45f306dc9c883UL);
    private static readonly double PiOverTwoFirst = FromBits(0x3ff921fb54400000UL);
    private static readonly double PiOverTwoFirstTail = FromBits(0x3dd0b4611a626331UL);
    private static readonly double PiOverTwoSecond = FromBits(0x3dd0b4611a600000UL);
    private static readonly double PiOverTwoSecondTail = FromBits(0x3ba3198a2e037073UL);
    private static readonly double PiOverTwoThird = FromBits(0x3ba3198a2e000000UL);
    private static readonly double PiOverTwoThirdTail = FromBits(0x397b839a252049c1UL);
    private static readonly double ExpP1 = FromBits(0x3fc555555555553eUL);
    private static readonly double ExpP2 = FromBits(0xbf66c16c16bebd93UL);
    private static readonly double ExpP3 = FromBits(0x3f11566aaf25de2cUL);
    private static readonly double ExpP4 = FromBits(0xbebbbd41c5d26bf1UL);
    private static readonly double ExpP5 = FromBits(0x3e66376972bea4d0UL);

    private static readonly double LogLg1 = FromBits(0x3fe5555555555593UL);
    private static readonly double LogLg2 = FromBits(0x3fd999999997fa04UL);
    private static readonly double LogLg3 = FromBits(0x3fd2492494229359UL);
    private static readonly double LogLg4 = FromBits(0x3fcc71c51d8e78afUL);
    private static readonly double LogLg5 = FromBits(0x3fc7466496cb03deUL);
    private static readonly double LogLg6 = FromBits(0x3fc39a09d078c69fUL);
    private static readonly double LogLg7 = FromBits(0x3fc2f112df3e5244UL);

    private static readonly double SinS1 = FromBits(0xbfc5555555555549UL);
    private static readonly double SinS2 = FromBits(0x3f8111111110f8a6UL);
    private static readonly double SinS3 = FromBits(0xbf2a01a019c161d5UL);
    private static readonly double SinS4 = FromBits(0x3ec71de357b1fe7dUL);
    private static readonly double SinS5 = FromBits(0xbe5ae5e68a2b9cebUL);
    private static readonly double SinS6 = FromBits(0x3de5d93a5acfd57cUL);
    private static readonly double CosC1 = FromBits(0x3fa555555555554cUL);
    private static readonly double CosC2 = FromBits(0xbf56c16c16c15177UL);
    private static readonly double CosC3 = FromBits(0x3efa01a019cb1590UL);
    private static readonly double CosC4 = FromBits(0xbe927e4f809c52adUL);
    private static readonly double CosC5 = FromBits(0x3e21ee9ebdb4b1c4UL);
    private static readonly double CosC6 = FromBits(0xbda8fae9be8838d4UL);

    /// <summary>Returns e raised to <paramref name="x"/>.</summary>
    public static double Exp(double x)
    {
        if (double.IsNaN(x))
        {
            return x;
        }

        if (double.IsPositiveInfinity(x))
        {
            return double.PositiveInfinity;
        }

        if (double.IsNegativeInfinity(x))
        {
            return 0;
        }

        ulong magnitudeBits = (ulong)BitConverter.DoubleToInt64Bits(x) & MagnitudeMask;
        if ((magnitudeBits >> 32) >= 0x40862e42U)
        {
            if (x > FromBits(0x40862e42fefa39efUL))
            {
                return double.PositiveInfinity;
            }

            if (x < FromBits(0xc0874910d52d3051UL))
            {
                return 0;
            }
        }

        int k;
        double high = 0;
        double low = 0;
        if ((magnitudeBits >> 32) > 0x3fd62e42U)
        {
            if ((magnitudeBits >> 32) < 0x3ff0a2b2U)
            {
                bool negative = x < 0;
                high = x - (negative ? -Ln2High : Ln2High);
                low = negative ? -Ln2Low : Ln2Low;
                k = negative ? -1 : 1;
            }
            else
            {
                k = (int)(InverseLn2 * x + (x < 0 ? -0.5 : 0.5));
                double dk = k;
                high = x - dk * Ln2High;
                low = dk * Ln2Low;
            }

            x = high - low;
        }
        else if (magnitudeBits < 0x3e30000000000000UL)
        {
            return 1 + x;
        }
        else
        {
            k = 0;
        }

        double z = x * x;
        double c = x - z * (ExpP1 + z * (ExpP2 + z * (ExpP3 + z * (ExpP4 + z * ExpP5))));
        double y;
        if (k == 0)
        {
            return 1 - ((x * c) / (c - 2) - x);
        }

        y = 1 - ((low - (x * c) / (2 - c)) - high);
        long adjustedBits = unchecked(BitConverter.DoubleToInt64Bits(y) + ((long)k << 52));
        if (k >= -1021)
        {
            return BitConverter.Int64BitsToDouble(adjustedBits);
        }

        adjustedBits = unchecked(BitConverter.DoubleToInt64Bits(y) + ((long)(k + 1000) << 52));
        y = BitConverter.Int64BitsToDouble(adjustedBits);
        return y * FromBits(0x0170000000000000UL);
    }

    /// <summary>Returns the natural logarithm of <paramref name="x"/>.</summary>
    public static double Log(double x)
    {
        if (double.IsNaN(x))
        {
            return x;
        }

        if (x == 0)
        {
            return double.NegativeInfinity;
        }

        if (x < 0)
        {
            return double.NaN;
        }

        if (double.IsPositiveInfinity(x))
        {
            return x;
        }

        long rawBits = BitConverter.DoubleToInt64Bits(x);
        uint highWord = (uint)((ulong)rawBits >> 32);
        uint lowWord = (uint)rawBits;
        int k = 0;
        if (highWord < 0x00100000U)
        {
            k -= 54;
            x *= FromBits(0x4350000000000000UL);
            rawBits = BitConverter.DoubleToInt64Bits(x);
            highWord = (uint)((ulong)rawBits >> 32);
            lowWord = (uint)rawBits;
        }

        k += (int)(highWord >> 20) - 1023;
        highWord &= 0x000fffffU;
        int adjust = (int)((highWord + 0x95f64U) & 0x100000U);
        ulong normalized = ((ulong)(highWord | ((uint)adjust ^ 0x3ff00000U)) << 32) | lowWord;
        x = BitConverter.Int64BitsToDouble(unchecked((long)normalized));
        k += adjust >> 20;

        double f = x - 1;
        if ((0x000fffffU & (2 + highWord)) < 3)
        {
            if (f == 0)
            {
                if (k == 0)
                {
                    return 0;
                }

                double integerExponent = k;
                return integerExponent * Ln2High + integerExponent * Ln2Low;
            }

            double smallR = f * f * (0.5 - 0.33333333333333333 * f);
            if (k == 0)
            {
                return f - smallR;
            }

            double smallExponent = k;
            return smallExponent * Ln2High - ((smallR - smallExponent * Ln2Low) - f);
        }

        double s = f / (2 + f);
        double exponent = k;
        double z = s * s;
        int i = unchecked((int)highWord) - 0x6147a;
        double w = z * z;
        int j = 0x6b851 - unchecked((int)highWord);
        double t1 = w * (LogLg2 + w * (LogLg4 + w * LogLg6));
        double t2 = z * (LogLg1 + w * (LogLg3 + w * (LogLg5 + w * LogLg7)));
        i |= j;
        double r = t2 + t1;
        if (i > 0)
        {
            double halfSquare = 0.5 * f * f;
            if (k == 0)
            {
                return f - (halfSquare - s * (halfSquare + r));
            }

            return exponent * Ln2High - ((halfSquare - (s * (halfSquare + r) + exponent * Ln2Low)) - f);
        }

        if (k == 0)
        {
            return f - s * (f - r);
        }

        return exponent * Ln2High - ((s * (f - r) - exponent * Ln2Low) - f);
    }

    /// <summary>Returns the correctly rounded non-negative square root.</summary>
    public static double Sqrt(double x)
    {
        long raw = BitConverter.DoubleToInt64Bits(x);
        ulong bits = (ulong)raw;
        ulong magnitude = bits & MagnitudeMask;
        if (double.IsNaN(x) || double.IsPositiveInfinity(x) || x == 0)
        {
            return x;
        }

        if ((bits & SignMask) != 0)
        {
            return double.NaN;
        }

        int exponentBits = (int)((magnitude >> 52) & 0x7ff);
        ulong significand;
        int binaryExponent;
        if (exponentBits == 0)
        {
            significand = magnitude & FractionMask;
            binaryExponent = -1074;
        }
        else
        {
            significand = (1UL << 52) | (magnitude & FractionMask);
            binaryExponent = exponentBits - 1023 - 52;
        }

        int inputExponent = BitLength(significand) - 1 + binaryExponent;
        int parity = inputExponent & 1;
        int scale = 105 - BitLength(significand) + parity;
        UInt128 scaledSquare = (UInt128)significand << scale;
        UInt128 roundedSignificand = IntegerSquareRoot(scaledSquare);
        UInt128 remainder = scaledSquare - roundedSignificand * roundedSignificand;
        if (remainder > roundedSignificand)
        {
            roundedSignificand++;
        }

        int resultExponent = inputExponent >> 1;
        if (roundedSignificand == ((UInt128)1 << 53))
        {
            roundedSignificand >>= 1;
            resultExponent++;
        }

        ulong resultExponentBits = (ulong)(resultExponent + 1023) << 52;
        ulong resultFraction = (ulong)roundedSignificand - (1UL << 52);
        return BitConverter.Int64BitsToDouble(unchecked((long)(resultExponentBits | resultFraction)));
    }

    /// <summary>Returns the sine of <paramref name="x"/>.</summary>
    public static double Sin(double x) => SinCos(x).Sin;

    /// <summary>Returns the cosine of <paramref name="x"/>.</summary>
    public static double Cos(double x) => SinCos(x).Cos;

    /// <summary>Computes sine and cosine with one deterministic argument reduction.</summary>
    public static (double Sin, double Cos) SinCos(double x)
    {
        if (double.IsNaN(x))
        {
            return (x, x);
        }

        if (double.IsInfinity(x))
        {
            return (double.NaN, double.NaN);
        }

        if (x == 0)
        {
            return (x, 1);
        }

        bool negative = x < 0;
        (int quadrant, double high, double low) = ReducePiOverTwo(Math.Abs(x));
        double sin = KernelSin(high, low);
        double cos = KernelCos(high, low);
        (double resultSin, double resultCos) = quadrant switch
        {
            0 => (sin, cos),
            1 => (cos, -sin),
            2 => (-sin, -cos),
            _ => (-cos, sin)
        };
        return (negative ? -resultSin : resultSin, resultCos);
    }

    private static (int Quadrant, double High, double Low) ReducePiOverTwo(double x)
    {
        ulong raw = (ulong)BitConverter.DoubleToInt64Bits(x);
        uint highWord = (uint)(raw >> 32);
        uint absoluteHighWord = highWord & 0x7fffffffU;
        if (absoluteHighWord <= 0x413921fbU)
        {
            return ReduceMediumPiOverTwo(x, absoluteHighWord);
        }

        int exponentBits = (int)((raw >> 52) & 0x7ff);
        ulong significand;
        int exponent;
        if (exponentBits == 0)
        {
            significand = raw & FractionMask;
            exponent = -1074;
        }
        else
        {
            significand = (1UL << 52) | (raw & FractionMask);
            exponent = exponentBits - 1023 - 52;
        }

        BigInteger quotientProduct = significand * TwoOverPi;
        int quotientShift = TrigScaleBits - exponent;
        BigInteger quotient = quotientProduct >> quotientShift;
        BigInteger remainder = quotientProduct - (quotient << quotientShift);
        BigInteger halfway = BigInteger.One << (quotientShift - 1);
        if (remainder > halfway || (remainder == halfway && !quotient.IsEven))
        {
            quotient++;
        }

        BigInteger scaledX = (BigInteger)significand << (exponent + TrigScaleBits);
        BigInteger scaledRemainder = scaledX - quotient * PiOverTwo;
        double high = BigIntegerToScaledDouble(scaledRemainder, TrigScaleBits);
        BigInteger highScaled = DoubleToScaledInteger(high, TrigScaleBits);
        double low = BigIntegerToScaledDouble(scaledRemainder - highScaled, TrigScaleBits);
        return ((int)(quotient & 3), high, low);
    }

    private static (int Quadrant, double High, double Low) ReduceMediumPiOverTwo(double x, uint absoluteHighWord)
    {
        if (absoluteHighWord <= 0x3fe921fbU)
        {
            return (0, x, 0);
        }

        double absolute = Math.Abs(x);
        int quadrant = (int)(absolute * InversePiOverTwo + 0.5);
        double turns = quadrant;
        double remainder = absolute - turns * PiOverTwoFirst;
        double correction = turns * PiOverTwoFirstTail;
        double high = remainder - correction;

        uint remainderHighWord = (uint)((ulong)BitConverter.DoubleToInt64Bits(high) >> 32) & 0x7fffffffU;
        int inputExponent = (int)(absoluteHighWord >> 20);
        int remainderExponent = (int)(remainderHighWord >> 20) & 0x7ff;
        if (inputExponent - remainderExponent > 16)
        {
            double firstRemainder = remainder;
            correction = turns * PiOverTwoSecond;
            remainder = firstRemainder - correction;
            correction = turns * PiOverTwoSecondTail - ((firstRemainder - remainder) - correction);
            high = remainder - correction;

            remainderHighWord = (uint)((ulong)BitConverter.DoubleToInt64Bits(high) >> 32) & 0x7fffffffU;
            remainderExponent = (int)(remainderHighWord >> 20) & 0x7ff;
            if (inputExponent - remainderExponent > 49)
            {
                double secondRemainder = remainder;
                correction = turns * PiOverTwoThird;
                remainder = secondRemainder - correction;
                correction = turns * PiOverTwoThirdTail - ((secondRemainder - remainder) - correction);
                high = remainder - correction;
            }
        }

        double low = (remainder - high) - correction;
        return (quadrant & 3, high, low);
    }

    private static double KernelSin(double x, double y)
    {
        double z = x * x;
        double v = z * x;
        double r = SinS2 + z * (SinS3 + z * (SinS4 + z * (SinS5 + z * SinS6)));
        return x - ((z * (0.5 * y - v * r) - y) - v * SinS1);
    }

    private static double KernelCos(double x, double y)
    {
        ulong absoluteBits = (ulong)BitConverter.DoubleToInt64Bits(x) & MagnitudeMask;
        double z = x * x;
        double r = z * (CosC1 + z * (CosC2 + z * (CosC3 + z * (CosC4 + z * (CosC5 + z * CosC6)))));
        if (absoluteBits < 0x3fd3333333333333UL)
        {
            return 1 - (0.5 * z - (z * r - x * y));
        }

        double qx = absoluteBits > 0x3fe9000000000000UL
            ? 0.28125
            : FromBits((ulong)BitConverter.DoubleToInt64Bits(Math.Abs(x) * 0.25) & 0xffffffff00000000UL);
        return (1 - qx) - ((0.5 * z - qx) - (z * r - x * y));
    }

    private static double BigIntegerToScaledDouble(BigInteger value, int fractionalBits)
    {
        if (value.IsZero)
        {
            return 0;
        }

        bool negative = value.Sign < 0;
        BigInteger magnitude = BigInteger.Abs(value);
        int bitLength = checked((int)magnitude.GetBitLength());
        int shift = Math.Max(0, bitLength - 53);
        BigInteger top = magnitude >> shift;
        if (shift > 0)
        {
            BigInteger remainder = magnitude - (top << shift);
            BigInteger halfway = BigInteger.One << (shift - 1);
            if (remainder > halfway || (remainder == halfway && !top.IsEven))
            {
                top++;
            }

            if (top == (BigInteger.One << 53))
            {
                top >>= 1;
                shift++;
            }
        }

        double result = ScaleByPowerOfTwo((double)(ulong)top, shift - fractionalBits);
        return negative ? -result : result;
    }

    private static BigInteger DoubleToScaledInteger(double value, int fractionalBits)
    {
        long raw = BitConverter.DoubleToInt64Bits(value);
        ulong bits = (ulong)raw;
        ulong magnitude = bits & MagnitudeMask;
        if (magnitude == 0)
        {
            return BigInteger.Zero;
        }

        int exponentBits = (int)((magnitude >> 52) & 0x7ff);
        ulong significand;
        int exponent;
        if (exponentBits == 0)
        {
            significand = magnitude & FractionMask;
            exponent = -1074;
        }
        else
        {
            significand = (1UL << 52) | (magnitude & FractionMask);
            exponent = exponentBits - 1023 - 52;
        }

        BigInteger scaled = (BigInteger)significand << (exponent + fractionalBits);
        return (bits & SignMask) == 0 ? scaled : -scaled;
    }

    private static double ScaleByPowerOfTwo(double value, int exponent)
    {
        if (value == 0 || !double.IsFinite(value))
        {
            return value;
        }

        while (exponent > 1023)
        {
            value *= FromBits(0x7fe0000000000000UL);
            exponent -= 1023;
        }

        while (exponent < -1022)
        {
            value *= FromBits(0x0010000000000000UL);
            exponent += 1022;
        }

        double power = FromBits((ulong)(exponent + 1023) << 52);
        return value * power;
    }

    private static UInt128 IntegerSquareRoot(UInt128 value)
    {
        int bitLength = BitLength(value);
        UInt128 root = (UInt128)1 << ((bitLength + 1) / 2);
        while (true)
        {
            UInt128 next = (root + value / root) >> 1;
            if (next >= root)
            {
                return root;
            }

            root = next;
        }
    }

    private static int BitLength(ulong value) => 64 - BitOperations.LeadingZeroCount(value);

    private static int BitLength(UInt128 value)
    {
        ulong high = (ulong)(value >> 64);
        return high != 0 ? 128 - BitOperations.LeadingZeroCount(high) : BitLength((ulong)value);
    }

    private static BigInteger ParseHex(string value) =>
        BigInteger.Parse("0" + value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static double FromBits(ulong bits) => BitConverter.Int64BitsToDouble(unchecked((long)bits));
}
