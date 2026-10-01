using System.Collections.ObjectModel;
using Rowles.Morphogenesis.Reference.Lattice;

namespace Rowles.Morphogenesis.Reference.Model;

public sealed class ReferenceState
{
    private readonly int[,] _cellIds;
    private readonly Dictionary<int, CellDefinition> _cellsById;
    private readonly ReadOnlyDictionary<int, CellDefinition> _readOnlyCellsById;
    private readonly CellTypeDefinition[] _cellTypes;
    private readonly ReadOnlyCollection<CellTypeDefinition> _readOnlyCellTypes;

    public ReferenceState(
        int[,] cellIds,
        IEnumerable<CellDefinition> cells,
        IEnumerable<CellTypeDefinition> cellTypes,
        ContactEnergyMatrix contactEnergies,
        ModelConventions? conventions = null)
    {
        ArgumentNullException.ThrowIfNull(cellIds);
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(cellTypes);
        ArgumentNullException.ThrowIfNull(contactEnergies);

        Width = cellIds.GetLength(0);
        Height = cellIds.GetLength(1);
        if (Width <= 0 || Height <= 0)
        {
            throw new ArgumentException("Lattice dimensions must be positive.", nameof(cellIds));
        }

        Conventions = conventions ?? ModelConventions.Canonical;
        Conventions.ValidateFor(Width, Height);

        _cellTypes = cellTypes.ToArray();
        ValidateCellTypes(_cellTypes, contactEnergies);
        _readOnlyCellTypes = Array.AsReadOnly(_cellTypes);

        ContactEnergies = contactEnergies;
        _cellsById = new Dictionary<int, CellDefinition>();
        foreach (CellDefinition cell in cells)
        {
            ValidateCellDefinition(cell, _cellTypes.Length);
            if (!_cellsById.TryAdd(cell.CellId, cell))
            {
                throw new ArgumentException($"Cell ID {cell.CellId} has duplicate metadata.", nameof(cells));
            }
        }

        _readOnlyCellsById = new ReadOnlyDictionary<int, CellDefinition>(_cellsById);
        _cellIds = (int[,])cellIds.Clone();
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                ValidateCellId(_cellIds[x, y]);
            }
        }
    }

    public int Width { get; }

    public int Height { get; }

    public int SiteCount => checked(Width * Height);

    public ModelConventions Conventions { get; }

    public ContactEnergyMatrix ContactEnergies { get; }

    public IReadOnlyDictionary<int, CellDefinition> CellsById => _readOnlyCellsById;

    public IReadOnlyList<CellTypeDefinition> CellTypes => _readOnlyCellTypes;

    public int CellIdAt(GridPoint point)
    {
        ValidatePoint(point);
        return _cellIds[point.X, point.Y];
    }

    public int CellIdAt(int x, int y) => CellIdAt(new GridPoint(x, y));

    public int CellTypeIdForCell(int cellId)
    {
        if (cellId == 0)
        {
            return 0;
        }

        return GetCellDefinition(cellId).CellTypeId;
    }

    public CellDefinition GetCellDefinition(int cellId)
    {
        if (cellId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellId), "Cell definitions are only available for biological IDs greater than zero.");
        }

        return _cellsById.TryGetValue(cellId, out CellDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"Biological cell ID {cellId} has no metadata.");
    }

    public int[,] GetCellIdsCopy() => (int[,])_cellIds.Clone();

    public ReferenceState Clone() => new(
        _cellIds,
        _cellsById.Values,
        _cellTypes,
        ContactEnergies,
        Conventions);

    public void SetCellId(GridPoint point, int cellId)
    {
        ValidatePoint(point);
        ValidateCellId(cellId);
        _cellIds[point.X, point.Y] = cellId;
    }

    public bool AreCopyNeighbours(GridPoint first, GridPoint second)
    {
        ValidatePoint(first);
        ValidatePoint(second);
        foreach (GridPoint offset in Conventions.CopyNeighbourhood.Offsets)
        {
            if (PeriodicLattice.Resolve(first, offset, Width, Height) == second)
            {
                return true;
            }
        }

        return false;
    }

    internal void ValidatePoint(GridPoint point)
    {
        if ((uint)point.X >= (uint)Width || (uint)point.Y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(point), $"Point {point} lies outside the {Width}x{Height} lattice.");
        }
    }

    private void ValidateCellId(int cellId)
    {
        if (cellId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellId), "Cell IDs cannot be negative.");
        }

        if (cellId > 0 && !_cellsById.ContainsKey(cellId))
        {
            throw new ArgumentException($"Biological cell ID {cellId} has no metadata.", nameof(cellId));
        }
    }

    private static void ValidateCellTypes(CellTypeDefinition[] cellTypes, ContactEnergyMatrix contactEnergies)
    {
        if (cellTypes.Length == 0 || contactEnergies.TypeCount != cellTypes.Length)
        {
            throw new ArgumentException("Contact-matrix dimensions must match the cell-type count.", nameof(contactEnergies));
        }

        if (cellTypes[0].TypeId != 0 || !string.Equals(cellTypes[0].Name, "Medium", StringComparison.Ordinal))
        {
            throw new ArgumentException("Cell type 0 must be named Medium.", nameof(cellTypes));
        }

        HashSet<string> names = new(StringComparer.Ordinal);
        for (int index = 0; index < cellTypes.Length; index++)
        {
            CellTypeDefinition type = cellTypes[index];
            if (type.TypeId != index)
            {
                throw new ArgumentException("Cell-type IDs must be contiguous matrix indexes starting at medium type 0.", nameof(cellTypes));
            }

            if (string.IsNullOrWhiteSpace(type.Name) || !names.Add(type.Name))
            {
                throw new ArgumentException("Cell-type names must be non-empty and unique.", nameof(cellTypes));
            }
        }
    }

    private static void ValidateCellDefinition(CellDefinition cell, int cellTypeCount)
    {
        if (cell.CellId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cell), "Biological cell IDs must be greater than zero.");
        }

        if ((uint)cell.CellTypeId >= (uint)cellTypeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(cell), $"Cell {cell.CellId} refers to an unknown cell type.");
        }

        if (!double.IsFinite(cell.TargetArea) || cell.TargetArea < 0 ||
            !double.IsFinite(cell.AreaStiffness) || cell.AreaStiffness < 0 ||
            !double.IsFinite(cell.TargetPerimeter) || cell.TargetPerimeter < 0 ||
            !double.IsFinite(cell.PerimeterStiffness) || cell.PerimeterStiffness < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cell), "Cell area/perimeter targets and stiffnesses must be finite and non-negative.");
        }
    }
}
