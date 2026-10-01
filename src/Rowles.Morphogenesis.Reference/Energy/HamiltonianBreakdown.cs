namespace Rowles.Morphogenesis.Reference.Energy;

public readonly record struct HamiltonianBreakdown(double Contact, double Area, double Perimeter)
{
    public double Total => Contact + Area + Perimeter;

    public static HamiltonianBreakdown operator -(HamiltonianBreakdown after, HamiltonianBreakdown before) =>
        new(after.Contact - before.Contact, after.Area - before.Area, after.Perimeter - before.Perimeter);
}

public static class EnergyComparison
{
    public const double AbsoluteTolerance = 1e-10;
    public const double RelativeTolerance = 1e-12;

    public static bool NearlyEqual(double left, double right)
    {
        double difference = Math.Abs(left - right);
        double scale = Math.Max(Math.Abs(left), Math.Abs(right));
        return difference <= AbsoluteTolerance + RelativeTolerance * scale;
    }
}
