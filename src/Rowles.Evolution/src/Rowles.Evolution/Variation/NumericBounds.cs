namespace Rowles.Evolution.Variation;

/// <summary>One finite, closed numeric solution interval.</summary>
public readonly record struct NumericBounds(double Lower, double Upper)
{
    public void Validate()
    {
        if (!double.IsFinite(Lower) || !double.IsFinite(Upper) || Upper <= Lower || !double.IsFinite(Upper - Lower))
            throw new ArgumentException("Solution bounds must be finite, have a finite width, and upper must exceed lower.");
    }
}
