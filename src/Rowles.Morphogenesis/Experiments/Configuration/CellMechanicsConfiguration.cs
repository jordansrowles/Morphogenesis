namespace Rowles.Morphogenesis.Experiments.Configuration;

public sealed record CellMechanicsConfiguration
{
    public required double TargetArea { get; init; }

    public required double AreaStiffness { get; init; }

    public required double TargetPerimeter { get; init; }

    public required double PerimeterStiffness { get; init; }
}
