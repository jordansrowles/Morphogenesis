namespace Rowles.Evolution.Archives;

/// <summary>Closed descriptor bounds and deterministic row-major archive dimensions.</summary>
public sealed class GridArchiveConfiguration
{
    private readonly double[] lowerBounds;
    private readonly double[] upperBounds;
    private readonly int[] binCounts;
    public GridArchiveConfiguration(double[] lowerBounds, double[] upperBounds, int[] binCounts,
        ObjectiveDirection objectiveDirection, double? objectiveBaseline = null, int maximumCells = 10_000_000)
    {
        ArgumentNullException.ThrowIfNull(lowerBounds);
        ArgumentNullException.ThrowIfNull(upperBounds);
        ArgumentNullException.ThrowIfNull(binCounts);
        if (lowerBounds.Length == 0 || lowerBounds.Length != upperBounds.Length || lowerBounds.Length != binCounts.Length)
            throw new ArgumentException("Descriptor bounds and bin counts must have the same non-zero dimensionality.");
        if (!Enum.IsDefined(objectiveDirection)) throw new ArgumentOutOfRangeException(nameof(objectiveDirection));
        if (objectiveBaseline is not null && !double.IsFinite(objectiveBaseline.Value))
            throw new ArgumentOutOfRangeException(nameof(objectiveBaseline));
        if (maximumCells <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCells));

        long product = 1;
        for (int i = 0; i < lowerBounds.Length; i++)
        {
            if (!double.IsFinite(lowerBounds[i]) || !double.IsFinite(upperBounds[i]) || upperBounds[i] <= lowerBounds[i] ||
                !double.IsFinite(upperBounds[i] - lowerBounds[i]))
                throw new ArgumentException($"Descriptor bound {i} must be finite with upper greater than lower.");
            if (binCounts[i] <= 0) throw new ArgumentOutOfRangeException(nameof(binCounts), "Every descriptor bin count must be positive.");
            product = checked(product * binCounts[i]);
            if (product > maximumCells) throw new ArgumentOutOfRangeException(nameof(binCounts), $"Archive would require {product} cells, exceeding the configured limit of {maximumCells}.");
        }

        this.lowerBounds = lowerBounds.ToArray();
        this.upperBounds = upperBounds.ToArray();
        this.binCounts = binCounts.ToArray();
        LowerBounds = Array.AsReadOnly(this.lowerBounds);
        UpperBounds = Array.AsReadOnly(this.upperBounds);
        BinCounts = Array.AsReadOnly(this.binCounts);
        CellCount = checked((int)product);
        ObjectiveDirection = objectiveDirection;
        ObjectiveBaseline = objectiveBaseline;
        MaximumCells = maximumCells;
    }

    public IReadOnlyList<double> LowerBounds { get; }
    public IReadOnlyList<double> UpperBounds { get; }
    public IReadOnlyList<int> BinCounts { get; }
    public int Dimension => binCounts.Length;
    public int CellCount { get; }
    public ObjectiveDirection ObjectiveDirection { get; }
    public double? ObjectiveBaseline { get; }
    public int MaximumCells { get; }

    public int GetCellIndex(IReadOnlyList<double> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        if (descriptors.Count != Dimension) throw new ArgumentException("Descriptor dimensionality does not match the archive.", nameof(descriptors));
        int index = 0;
        for (int d = 0; d < Dimension; d++)
        {
            double value = descriptors[d];
            if (!double.IsFinite(value) || value < lowerBounds[d] || value > upperBounds[d])
                throw new ArgumentOutOfRangeException(nameof(descriptors), $"Descriptor {d} is non-finite or outside its closed bounds.");
            int bin = value == upperBounds[d]
                ? binCounts[d] - 1
                : (int)Math.Floor((value - lowerBounds[d]) / (upperBounds[d] - lowerBounds[d]) * binCounts[d]);
            if ((uint)bin >= (uint)binCounts[d]) throw new ArgumentOutOfRangeException(nameof(descriptors), $"Descriptor {d} did not map to a valid bin.");
            index = checked(index * binCounts[d] + bin);
        }

        return index;
    }
}
