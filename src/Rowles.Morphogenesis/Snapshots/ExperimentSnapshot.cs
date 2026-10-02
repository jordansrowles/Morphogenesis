using Rowles.Morphogenesis.Serialisation;
using System.Text.Json;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Configuration;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Snapshots;

/// <summary>A compact diagnostic view of one experiment state; it is not a restart checkpoint.</summary>
public sealed record ExperimentSnapshot(
    int SnapshotSchemaVersion,
    string ModelVersion,
    string ExperimentId,
    string ReplicateId,
    int ReplicateIndex,
    ulong Seed,
    long Mcs,
    int GridWidth,
    int GridHeight,
    int[] CellIds,
    SnapshotCellState[] Cells,
    CellTypeDefinition[] CellTypes)
{
    public const int CurrentSchemaVersion = 1;

    public const string CurrentModelVersion = "serial-cpm-v1";

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(CellIds);
        ArgumentNullException.ThrowIfNull(Cells);
        ArgumentNullException.ThrowIfNull(CellTypes);

        if (SnapshotSchemaVersion != CurrentSchemaVersion)
        {
            throw new NotSupportedException($"Snapshot schema version {SnapshotSchemaVersion} is not supported.");
        }

        if (ModelVersion != CurrentModelVersion)
        {
            throw new NotSupportedException($"Snapshot model version '{ModelVersion}' is not supported.");
        }

        if (string.IsNullOrWhiteSpace(ExperimentId) || string.IsNullOrWhiteSpace(ReplicateId) || ReplicateIndex < 0 || Mcs < 0)
        {
            throw new ArgumentException("Snapshot identity and MCS values are invalid.");
        }

        if (GridWidth <= 0 || GridHeight <= 0 || CellIds.Length != checked(GridWidth * GridHeight))
        {
            throw new ArgumentException("Snapshot lattice dimensions do not match the cell ID array.");
        }

        if (CellTypes.Length == 0 || CellTypes.Any(type => type is null))
        {
            throw new ArgumentException("Snapshot cell type mapping is invalid.");
        }

        HashSet<string> typeNames = new(StringComparer.OrdinalIgnoreCase);
        for (int typeId = 0; typeId < CellTypes.Length; typeId++)
        {
            if (CellTypes[typeId].TypeId != typeId || string.IsNullOrWhiteSpace(CellTypes[typeId].Name) ||
                !typeNames.Add(CellTypes[typeId].Name.Trim()))
            {
                throw new ArgumentException("Snapshot type IDs must be contiguous and names unique.");
            }
        }

        if (CellIds.Any(cellId => cellId < 0))
        {
            throw new ArgumentException("Snapshot lattice cannot contain negative cell IDs.");
        }

        int maxId = CellIds.Max();
        bool[] registered = new bool[maxId + 1];
        foreach (SnapshotCellState cell in Cells)
        {
            if (cell.CellId <= 0 || cell.CellId >= registered.Length || registered[cell.CellId])
            {
                throw new ArgumentException("Snapshot cell IDs must be unique and positive.");
            }

            if ((uint)cell.CellTypeId >= (uint)CellTypes.Length || CellTypes[cell.CellTypeId].TypeId != cell.CellTypeId)
            {
                throw new ArgumentException($"Snapshot cell {cell.CellId} has no matching cell type mapping.");
            }

            if (!cell.IsAlive || cell.Area <= 0 || cell.Perimeter < 0 ||
                !double.IsFinite(cell.TargetArea) || cell.TargetArea < 0 ||
                !double.IsFinite(cell.AreaStiffness) || cell.AreaStiffness < 0 ||
                !double.IsFinite(cell.TargetPerimeter) || cell.TargetPerimeter < 0 ||
                !double.IsFinite(cell.PerimeterStiffness) || cell.PerimeterStiffness < 0)
            {
                throw new ArgumentException($"Snapshot cell {cell.CellId} has invalid state metadata.");
            }

            registered[cell.CellId] = true;
        }

        for (int index = 0; index < CellIds.Length; index++)
        {
            int cellId = CellIds[index];
            if (cellId < 0 || cellId >= registered.Length || (cellId > 0 && !registered[cellId]))
            {
                throw new ArgumentException($"Snapshot lattice site {index} refers to an unknown cell ID.");
            }
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, MorphogenesisJsonContext.Instance.ExperimentSnapshot);

    public static ExperimentSnapshot FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        ExperimentSnapshot snapshot = JsonSerializer.Deserialize(json, MorphogenesisJsonContext.Instance.ExperimentSnapshot)
            ?? throw new JsonException("The experiment snapshot was JSON null.");
        snapshot.Validate();
        return snapshot;
    }

    public static ExperimentSnapshot Capture(
        ExperimentManifest manifest,
        string replicateId,
        int replicateIndex,
        ulong seed,
        long mcs,
        MorphogenesisState state)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(state);
        manifest.Validate();
        int[] ids = state.GetCellIdsCopy();
        int[] liveIds = ids.Where(cellId => cellId > 0).Distinct().Order().ToArray();
        SnapshotCellState[] cells = liveIds.Select(cellId =>
        {
            CellState cell = state.GetCellState(cellId);
            return new SnapshotCellState(
                cell.CellId,
                cell.CellTypeId,
                cell.IsAlive,
                cell.Area,
                cell.Perimeter,
                cell.TargetArea,
                cell.AreaStiffness,
                cell.TargetPerimeter,
                cell.PerimeterStiffness);
        }).ToArray();
        ExperimentSnapshot snapshot = new(
            CurrentSchemaVersion,
            CurrentModelVersion,
            manifest.ExperimentId,
            replicateId,
            replicateIndex,
            seed,
            mcs,
            state.Width,
            state.Height,
            ids,
            cells,
            manifest.CellTypes.ToArray());
        snapshot.Validate();
        return snapshot;
    }

}
