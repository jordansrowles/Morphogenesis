using Rowles.Evolution.Archives;
using Rowles.Evolution.Random;

namespace Rowles.Evolution.Variation;

/// <summary>
/// Iso+LineDD mutation. Each child follows x' = x_i + sigma_iso*N(0,I) +
/// sigma_line*(x_j-x_i)*N(0,1), with two uniformly selected occupied elites.
/// Formula from Vassiliades and Mouret, GECCO 2018, equation 3,
/// https://doi.org/10.1145/3205455.3205602.
/// </summary>
public sealed class IsoLineVariation
{
    private readonly NumericBounds[] bounds;

    public IsoLineVariation(IEnumerable<NumericBounds> bounds, double sigmaIso = 0.01, double sigmaLine = 0.2)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        this.bounds = bounds.ToArray();
        if (this.bounds.Length == 0) throw new ArgumentException("At least one solution dimension is required.", nameof(bounds));
        foreach (NumericBounds bound in this.bounds) bound.Validate();
        if (!double.IsFinite(sigmaIso) || sigmaIso < 0) throw new ArgumentOutOfRangeException(nameof(sigmaIso));
        if (!double.IsFinite(sigmaLine) || sigmaLine < 0) throw new ArgumentOutOfRangeException(nameof(sigmaLine));
        SigmaIso = sigmaIso;
        SigmaLine = sigmaLine;
    }

    public double SigmaIso { get; }
    public double SigmaLine { get; }
    public IReadOnlyList<NumericBounds> Bounds => Array.AsReadOnly(bounds);

    public double[] CreateRandom(EvolutionRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        double[] values = new double[bounds.Length];
        for (int i = 0; i < values.Length; i++)
            values[i] = bounds[i].Lower + random.NextDouble() * (bounds[i].Upper - bounds[i].Lower);
        return values;
    }

    public double[] Mutate(GridElite first, GridElite second, EvolutionRandom random)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(random);
        if (first.Solution.Count != bounds.Length || second.Solution.Count != bounds.Length)
            throw new ArgumentException("Parent dimensionality does not match variation bounds.");
        double lineScale = random.NextGaussian();
        double[] child = new double[bounds.Length];
        for (int i = 0; i < child.Length; i++)
        {
            double value = first.Solution[i] + SigmaIso * random.NextGaussian() +
                SigmaLine * (second.Solution[i] - first.Solution[i]) * lineScale;
            if (!double.IsFinite(value)) throw new ArithmeticException("Iso+Line generated a non-finite value from finite parents.");
            child[i] = Reflect(value, bounds[i]);
        }

        return child;
    }

    /// <summary>Reflects through the closed interval using a triangular wave, without resampling.</summary>
    public static double Reflect(double value, NumericBounds bound)
    {
        bound.Validate();
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (value >= bound.Lower && value <= bound.Upper) return value;

        // Half-scale first so subtraction remains finite even when opposite-signed inputs are near double limits.
        double lowerHalf = bound.Lower * 0.5;
        double widthHalf = bound.Upper * 0.5 - lowerHalf;
        double offset = value * 0.5 - lowerHalf;
        double period = widthHalf * 2;
        if (double.IsFinite(period))
        {
            offset %= period;
            if (offset < 0) offset += period;
            if (offset > widthHalf) offset = period - offset;
        }
        else
        {
            // Here the full interval is wider than half the representable range, so finite input can cross at most one edge.
            if (offset < 0) offset = -offset;
            else if (offset > widthHalf) offset = 2 * widthHalf - offset;
        }

        double reflected = (lowerHalf + offset) * 2;
        return Math.Clamp(reflected, bound.Lower, bound.Upper);
    }
}
