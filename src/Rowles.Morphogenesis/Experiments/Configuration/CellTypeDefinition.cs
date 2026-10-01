namespace Rowles.Morphogenesis.Experiments.Configuration;

public sealed record CellTypeDefinition
{
    public required int TypeId { get; init; }

    public required string Name { get; init; }
}
