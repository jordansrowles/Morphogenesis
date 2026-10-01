namespace Rowles.Morphogenesis.Reference.Energy;

public readonly record struct HamiltonianBreakdown(double Contact, double Area, double Perimeter)
{
    public double Total => Contact + Area + Perimeter;

    public static HamiltonianBreakdown operator -(HamiltonianBreakdown after, HamiltonianBreakdown before) =>
        new(after.Contact - before.Contact, after.Area - before.Area, after.Perimeter - before.Perimeter);
}
