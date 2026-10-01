using Rowles.Morphogenesis.Measurements.Metrics;

namespace Rowles.Morphogenesis.Measurements;

public sealed record TissueMeasurements(
    long HeterotypicInterfaceCount,
    long TotalCellCellInterfaceCount,
    double HeterotypicInterfaceFraction,
    TypeInterfaceCount[] HomotypicInterfacesByType,
    int TypeACellCount,
    int TypeBCellCount,
    double MeanCellArea,
    int MinimumCellArea,
    int MaximumCellArea,
    double MeanCellPerimeter,
    int MinimumCellPerimeter,
    int MaximumCellPerimeter,
    TypeDomainCount[] DomainsByType);
