using Rowles.Evolution.Archives;
using Rowles.Evolution.Variation;

namespace Rowles.Evolution.Algorithms;

public sealed class GridMapElitesConfiguration
{
    private readonly NumericBounds[] solutionBounds;

    public GridMapElitesConfiguration(IEnumerable<NumericBounds> solutionBounds, GridArchiveConfiguration archive,
        double sigmaIso = 0.01, double sigmaLine = 0.2)
    {
        ArgumentNullException.ThrowIfNull(solutionBounds);
        this.solutionBounds = solutionBounds.ToArray();
        if (this.solutionBounds.Length == 0) throw new ArgumentException("At least one solution dimension is required.", nameof(solutionBounds));
        foreach (NumericBounds bound in this.solutionBounds) bound.Validate();
        Archive = archive ?? throw new ArgumentNullException(nameof(archive));
        SolutionBounds = Array.AsReadOnly(this.solutionBounds);
        Variation = new IsoLineVariation(this.solutionBounds, sigmaIso, sigmaLine);
    }

    public IReadOnlyList<NumericBounds> SolutionBounds { get; }
    public GridArchiveConfiguration Archive { get; }
    public IsoLineVariation Variation { get; }
}
