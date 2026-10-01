namespace Rowles.Morphogenesis.Experiments.Configuration;

public sealed record InitialiserConfiguration
{
    public required InitialiserKind Kind { get; init; }

    public required int CellCount { get; init; }

    public required int ApproximateTargetCellArea { get; init; }

    public required int TypeAId { get; init; }

    public required int TypeBId { get; init; }

    public required double TypeAProportion { get; init; }
}
