namespace Rowles.Morphogenesis.Measurements.Metrics;

public static class MeasurementCatalog
{
    public static IReadOnlyList<MetricDefinition> Definitions { get; } =
    [
        new("heterotypic-interface-fraction", "Heterotypic cell-cell interface fraction", "neighbour-pair fraction"),
        new("heterotypic-interface-count", "Heterotypic cell-cell interface count", "unordered lattice neighbour pairs"),
        new("total-cell-cell-interface-count", "Total cell-cell interface count", "unordered lattice neighbour pairs"),
        new("total-homotypic-interface-count", "Total homotypic cell-cell interface count", "unordered lattice neighbour pairs"),
        new("homotypic-interface-count-by-type", "Homotypic interface count by type", "unordered lattice neighbour pairs"),
        new("cell-count-by-type", "Biological cell count by type", "cells"),
        new("cell-area", "Cell area summary", "lattice sites"),
        new("cell-perimeter", "Cell perimeter summary", "configured perimeter stencil contributions"),
        new("type-domain-count", "Connected type-domain count", "domains")
    ];
}
