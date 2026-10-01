using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Model;

namespace Rowles.Morphogenesis.Reference.Tests.Fixtures;

internal static class TestStateFactory
{
    private static readonly CellTypeDefinition[] Types =
    [
        new(0, "Medium"),
        new(1, "A"),
        new(2, "B")
    ];

    private static readonly double[,] DefaultContacts =
    {
        { 0, 3, 4 },
        { 3, 2, 5 },
        { 4, 5, 1 }
    };

    public static ReferenceState FromRows(
        string[] rows,
        IReadOnlyDictionary<int, int>? cellTypes = null,
        IEnumerable<CellDefinition>? definitions = null,
        double[,]? contactEnergies = null,
        ContactCouplingNeighbourhood? coupling = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Length == 0 || rows[0].Length == 0 || rows.Any(row => row.Length != rows[0].Length))
        {
            throw new ArgumentException("Fixture rows must be non-empty and have equal width.", nameof(rows));
        }

        int width = rows[0].Length;
        int height = rows.Length;
        int[,] ids = new int[width, height];
        for (int row = 0; row < height; row++)
        {
            int y = height - 1 - row;
            for (int x = 0; x < width; x++)
            {
                ids[x, y] = rows[row][x] switch
                {
                    '.' => 0,
                    'A' => 1,
                    'a' => 2,
                    'B' => 3,
                    _ => throw new ArgumentException($"Unsupported fixture symbol '{rows[row][x]}'.", nameof(rows))
                };
            }
        }

        HashSet<int> present = [];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (ids[x, y] > 0)
                {
                    present.Add(ids[x, y]);
                }
            }
        }

        CellDefinition[] supplied = definitions?.ToArray() ?? [];
        Dictionary<int, CellDefinition> byId = supplied.ToDictionary(cell => cell.CellId);
        foreach (int id in present)
        {
            if (!byId.ContainsKey(id))
            {
                int type = cellTypes is not null && cellTypes.TryGetValue(id, out int mappedType) ? mappedType : 1;
                byId.Add(id, DefaultCell(id, type));
            }
        }

        ContactEnergyMatrix matrix = new(contactEnergies ?? DefaultContacts);
        ModelConventions conventions = coupling is null
            ? ModelConventions.Canonical
            : new ModelConventions(
                CopyNeighbourhood.VonNeumann,
                coupling,
                PerimeterNeighbourhood.Moore,
                ConnectivityAdjacency.VonNeumann);
        return new ReferenceState(ids, byId.Values, Types, matrix, conventions);
    }

    public static CellDefinition DefaultCell(int cellId, int cellTypeId = 1) =>
        new(cellId, cellTypeId, TargetArea: 2, AreaStiffness: 1, TargetPerimeter: 8, PerimeterStiffness: 0.25);
}
