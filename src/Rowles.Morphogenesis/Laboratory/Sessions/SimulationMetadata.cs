using Rowles.Morphogenesis.Experiments.Configuration;
using Rowles.Morphogenesis.Lattice;

namespace Rowles.Morphogenesis.Laboratory.Sessions;

public sealed record SimulationMetadata
{
    private readonly CellTypeDefinition[] _cellTypes;
    private readonly int[] _cellTypeByCellId;

    public SimulationMetadata(
        Guid sessionId,
        SimulationRunIdentity runIdentity,
        int gridWidth,
        int gridHeight,
        BoundaryMode boundaryMode,
        CopyNeighbourhood copyNeighbourhood,
        ContactCouplingNeighbourhood contactCouplingNeighbourhood,
        PerimeterNeighbourhood perimeterNeighbourhood,
        ConnectivityAdjacency connectivityAdjacency,
        int experimentSchemaVersion,
        CellTypeDefinition[] cellTypes,
        int[] cellTypeByCellId)
    {
        ArgumentNullException.ThrowIfNull(runIdentity);
        ArgumentNullException.ThrowIfNull(cellTypes);
        ArgumentNullException.ThrowIfNull(cellTypeByCellId);
        SessionId = sessionId;
        RunIdentity = runIdentity;
        GridWidth = gridWidth;
        GridHeight = gridHeight;
        BoundaryMode = boundaryMode;
        CopyNeighbourhood = copyNeighbourhood;
        ContactCouplingNeighbourhood = contactCouplingNeighbourhood;
        PerimeterNeighbourhood = perimeterNeighbourhood;
        ConnectivityAdjacency = connectivityAdjacency;
        ExperimentSchemaVersion = experimentSchemaVersion;
        _cellTypes = (CellTypeDefinition[])cellTypes.Clone();
        _cellTypeByCellId = (int[])cellTypeByCellId.Clone();
    }

    public Guid SessionId { get; }

    public SimulationRunIdentity RunIdentity { get; }

    public int GridWidth { get; }

    public int GridHeight { get; }

    public BoundaryMode BoundaryMode { get; }

    public CopyNeighbourhood CopyNeighbourhood { get; }

    public ContactCouplingNeighbourhood ContactCouplingNeighbourhood { get; }

    public PerimeterNeighbourhood PerimeterNeighbourhood { get; }

    public ConnectivityAdjacency ConnectivityAdjacency { get; }

    public int ExperimentSchemaVersion { get; }

    public CellTypeDefinition[] CellTypes => (CellTypeDefinition[])_cellTypes.Clone();

    public int[] CellTypeByCellId => (int[])_cellTypeByCellId.Clone();
}
