using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;
using ReferenceCellDefinition = Rowles.Morphogenesis.Reference.Model.CellDefinition;
using ReferenceCellTypeDefinition = Rowles.Morphogenesis.Reference.Model.CellTypeDefinition;
using ReferenceContactEnergyMatrix = Rowles.Morphogenesis.Reference.Model.ContactEnergyMatrix;
using ReferenceModelConventions = Rowles.Morphogenesis.Reference.Lattice.ModelConventions;
using ReferenceState = Rowles.Morphogenesis.Reference.Model.ReferenceState;
using ReferenceCopyNeighbourhood = Rowles.Morphogenesis.Reference.Lattice.CopyNeighbourhood;
using ReferenceContactNeighbourhood = Rowles.Morphogenesis.Reference.Lattice.ContactCouplingNeighbourhood;
using ReferencePerimeterNeighbourhood = Rowles.Morphogenesis.Reference.Lattice.PerimeterNeighbourhood;
using ReferenceConnectivityAdjacency = Rowles.Morphogenesis.Reference.Lattice.ConnectivityAdjacency;

namespace Rowles.Morphogenesis.Tests.Fixtures;

internal static class ProductionTestStateFactory
{
    internal static MorphogenesisState FromRows(
        string[] rows,
        CellDefinition[] cells,
        double[,] contacts,
        SimulationConfiguration? configuration = null)
    {
        int width = rows[0].Length;
        int height = rows.Length;
        int[] ids = ToFlatIds(rows);
        return new MorphogenesisState(width, height, ids, cells, new ContactEnergyMatrix(contacts), configuration);
    }

    internal static ReferenceState ToReference(
        string[] rows,
        CellDefinition[] cells,
        double[,] contacts)
    {
        int width = rows[0].Length;
        int height = rows.Length;
        int[] flat = ToFlatIds(rows);
        int[,] ids = new int[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                ids[x, y] = flat[y * width + x];
            }
        }

        ReferenceCellDefinition[] referenceCells = new ReferenceCellDefinition[cells.Length];
        for (int index = 0; index < cells.Length; index++)
        {
            CellDefinition cell = cells[index];
            referenceCells[index] = new ReferenceCellDefinition(
                cell.CellId,
                cell.CellTypeId,
                cell.TargetArea,
                cell.AreaStiffness,
                cell.TargetPerimeter,
                cell.PerimeterStiffness);
        }

        ReferenceCellTypeDefinition[] types = new ReferenceCellTypeDefinition[contacts.GetLength(0)];
        types[0] = new ReferenceCellTypeDefinition(0, "Medium");
        for (int type = 1; type < types.Length; type++)
        {
            types[type] = new ReferenceCellTypeDefinition(type, $"Type {type}");
        }

        return new ReferenceState(
            ids,
            referenceCells,
            types,
            new ReferenceContactEnergyMatrix(contacts),
            new ReferenceModelConventions(
                ReferenceCopyNeighbourhood.VonNeumann,
                ReferenceContactNeighbourhood.Moore,
                ReferencePerimeterNeighbourhood.Moore,
                ReferenceConnectivityAdjacency.VonNeumann));
    }

    internal static int[] ToFlatIds(string[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Length == 0 || rows[0].Length == 0 || rows.Any(row => row.Length != rows[0].Length))
        {
            throw new ArgumentException("Fixture rows must be non-empty and have equal width.", nameof(rows));
        }

        int width = rows[0].Length;
        int height = rows.Length;
        int[] ids = new int[checked(width * height)];
        for (int row = 0; row < height; row++)
        {
            int y = height - 1 - row;
            for (int x = 0; x < width; x++)
            {
                ids[y * width + x] = rows[row][x] switch
                {
                    '.' => 0,
                    'A' => 1,
                    'a' => 2,
                    'B' => 3,
                    _ => throw new ArgumentException($"Unsupported fixture symbol '{rows[row][x]}'.", nameof(rows))
                };
            }
        }

        return ids;
    }

    internal static string[] ToRows(MorphogenesisState state)
    {
        string[] rows = new string[state.Height];
        for (int row = 0; row < state.Height; row++)
        {
            int y = state.Height - 1 - row;
            char[] symbols = new char[state.Width];
            for (int x = 0; x < state.Width; x++)
            {
                symbols[x] = state.CellIdAt(x, y) switch
                {
                    0 => '.',
                    1 => 'A',
                    2 => 'a',
                    3 => 'B',
                    _ => throw new InvalidDataException("Unexpected cell ID in row fixture.")
                };
            }

            rows[row] = new string(symbols);
        }

        return rows;
    }
}
