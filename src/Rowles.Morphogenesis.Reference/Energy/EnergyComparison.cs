namespace Rowles.Morphogenesis.Reference.Energy;

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
