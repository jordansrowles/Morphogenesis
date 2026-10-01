using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Configuration;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Random;
using Rowles.StrictMaths;

namespace Rowles.Morphogenesis.Initialisation;

/// <summary>Builds a compact deterministic rectangle of connected cells and shuffles their A/B labels.</summary>
public static class PackedAggregateInitialiser
{
    public static PackedAggregateInitialisation Create(ExperimentManifest manifest, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();

        (int cellWidth, int cellHeight, int columns, int rows) = SelectPacking(manifest);
        int aggregateWidth = checked(cellWidth * columns);
        int aggregateHeight = checked(cellHeight * rows);
        int startX = (manifest.GridWidth - aggregateWidth) / 2;
        int startY = (manifest.GridHeight - aggregateHeight) / 2;
        int[] lattice = new int[checked(manifest.GridWidth * manifest.GridHeight)];

        int typeACount = (int)Math.Floor(manifest.Initialiser.CellCount * manifest.Initialiser.TypeAProportion + 0.5);
        int typeBCount = manifest.Initialiser.CellCount - typeACount;
        int[] cellTypes = new int[manifest.Initialiser.CellCount];
        for (int index = 0; index < typeACount; index++)
        {
            cellTypes[index] = manifest.Initialiser.TypeAId;
        }

        for (int index = typeACount; index < cellTypes.Length; index++)
        {
            cellTypes[index] = manifest.Initialiser.TypeBId;
        }

        Xoshiro256StarStar random = new(seed);
        for (int index = cellTypes.Length - 1; index > 0; index--)
        {
            int other = random.NextInt(index + 1);
            (cellTypes[index], cellTypes[other]) = (cellTypes[other], cellTypes[index]);
        }

        CellDefinition[] cells = new CellDefinition[manifest.Initialiser.CellCount];
        for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
        {
            int cellId = cellIndex + 1;
            cells[cellIndex] = new CellDefinition(
                cellId,
                cellTypes[cellIndex],
                manifest.Mechanics.TargetArea,
                manifest.Mechanics.AreaStiffness,
                manifest.Mechanics.TargetPerimeter,
                manifest.Mechanics.PerimeterStiffness);

            int cellRow = cellIndex / columns;
            int cellColumn = cellIndex % columns;
            int firstX = startX + cellColumn * cellWidth;
            int firstY = startY + cellRow * cellHeight;
            for (int y = firstY; y < firstY + cellHeight; y++)
            {
                for (int x = firstX; x < firstX + cellWidth; x++)
                {
                    int site = y * manifest.GridWidth + x;
                    if (lattice[site] != 0)
                    {
                        throw new InvalidOperationException("Packed aggregate construction produced overlapping cell IDs.");
                    }

                    lattice[site] = cellId;
                }
            }
        }

        SimulationConfiguration simulationConfiguration = new(
            manifest.BoundaryMode,
            new LatticeConventions(
                manifest.CopyNeighbourhood,
                manifest.ContactCouplingNeighbourhood,
                manifest.PerimeterNeighbourhood,
                manifest.ConnectivityAdjacency));
        MorphogenesisState state = new(
            manifest.GridWidth,
            manifest.GridHeight,
            lattice,
            cells,
            CreateContactMatrix(manifest.ContactEnergies),
            simulationConfiguration);
        state.ValidateInvariants();

        return new PackedAggregateInitialisation(
            state,
            typeACount,
            typeBCount,
            checked(cellWidth * cellHeight),
            cellWidth,
            cellHeight,
            aggregateWidth,
            aggregateHeight);
    }

    private static (int CellWidth, int CellHeight, int Columns, int Rows) SelectPacking(ExperimentManifest manifest)
    {
        int squareWidth = checked((int)Math.Ceiling(StrictMath.Sqrt(manifest.Initialiser.ApproximateTargetCellArea)));
        int squareHeight = checked((int)Math.Ceiling((double)manifest.Initialiser.ApproximateTargetCellArea / squareWidth));
        Packing? best = null;

        ConsiderOrientation(squareWidth, squareHeight);
        if (squareWidth != squareHeight)
        {
            ConsiderOrientation(squareHeight, squareWidth);
        }

        if (best is null)
        {
            throw new ArgumentException("The requested packed aggregate cannot fit on this lattice.", nameof(manifest));
        }

        return (best.Value.CellWidth, best.Value.CellHeight, best.Value.Columns, best.Value.Rows);

        void ConsiderOrientation(int cellWidth, int cellHeight)
        {
            for (int columns = 1; columns <= manifest.Initialiser.CellCount; columns++)
            {
            int rows = (int)(((long)manifest.Initialiser.CellCount + columns - 1) / columns);
                long aggregateWidth = (long)columns * cellWidth;
                long aggregateHeight = (long)rows * cellHeight;
                if (aggregateWidth > manifest.GridWidth || aggregateHeight > manifest.GridHeight)
                {
                    continue;
                }

                double aspectPenalty = Math.Abs(StrictMath.Log((double)aggregateWidth / aggregateHeight));
                double unusedSlotPenalty = (double)((long)rows * columns - manifest.Initialiser.CellCount) / manifest.Initialiser.CellCount;
                double score = aspectPenalty + unusedSlotPenalty * 0.05;
                Packing candidate = new(cellWidth, cellHeight, columns, rows, score);
                if (best is null || candidate.Score < best.Value.Score)
                {
                    best = candidate;
                }
            }
        }
    }

    private static ContactEnergyMatrix CreateContactMatrix(double[][] values)
    {
        int size = values.Length;
        double[,] matrix = new double[size, size];
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                matrix[row, column] = values[row][column];
            }
        }

        return new ContactEnergyMatrix(matrix);
    }

    private readonly record struct Packing(int CellWidth, int CellHeight, int Columns, int Rows, double Score);
}
