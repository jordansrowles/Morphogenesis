using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Initialisation;

public sealed record PackedAggregateInitialisation(
    MorphogenesisState State,
    int TypeACellCount,
    int TypeBCellCount,
    int ActualCellArea,
    int CellWidth,
    int CellHeight,
    int AggregateWidth,
    int AggregateHeight);
