using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Measurements.Metrics;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Measurements;

/// <summary>Measures unordered cell-cell neighbour pairs; medium contacts are excluded.</summary>
public static class TissueMeasurementCalculator
{
    public static TissueMeasurements Measure(
        MorphogenesisState state,
        int typeAId,
        int typeBId,
        ContactCouplingNeighbourhood neighbourhood)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (typeAId <= 0 || typeAId >= state.ContactEnergies.TypeCount ||
            typeBId <= 0 || typeBId >= state.ContactEnergies.TypeCount || typeAId == typeBId)
        {
            throw new ArgumentOutOfRangeException(nameof(typeAId), "A and B must be distinct biological cell types in the contact matrix.");
        }

        if (!Enum.IsDefined(neighbourhood))
        {
            throw new ArgumentOutOfRangeException(nameof(neighbourhood));
        }

        int[] lattice = state.GetCellIdsCopy();
        int maxCellId = lattice.Max();
        bool[] present = new bool[maxCellId + 1];
        CellState[] cellStates = new CellState[maxCellId + 1];
        for (int index = 0; index < lattice.Length; index++)
        {
            int cellId = lattice[index];
            if (cellId <= 0 || present[cellId])
            {
                continue;
            }

            present[cellId] = true;
            cellStates[cellId] = state.GetCellState(cellId);
        }

        long heterotypicCount = 0;
        long totalCellCellCount = 0;
        long[] homotypicCounts = new long[state.ContactEnergies.TypeCount];
        List<int>[] sameTypeNeighbours = new List<int>[cellStates.Length];
        for (int cellId = 1; cellId < present.Length; cellId++)
        {
            if (present[cellId])
            {
                sameTypeNeighbours[cellId] = [];
            }
        }

        int offsetCount = StencilGeometry.Count(neighbourhood);
        for (int index = 0; index < lattice.Length; index++)
        {
            int firstCellId = lattice[index];
            for (int offsetIndex = 0; offsetIndex < offsetCount; offsetIndex++)
            {
                StencilGeometry.ContactOffset(neighbourhood, offsetIndex, out int dx, out int dy);
                if (!StencilGeometry.IsPositiveHalf(dx, dy))
                {
                    continue;
                }

                int neighbourIndex = state.Resolve(index, dx, dy);
                if (neighbourIndex < 0)
                {
                    continue;
                }

                int secondCellId = lattice[neighbourIndex];
                if (firstCellId == 0 || secondCellId == 0 || firstCellId == secondCellId)
                {
                    continue;
                }

                totalCellCellCount++;
                int firstType = cellStates[firstCellId].CellTypeId;
                int secondType = cellStates[secondCellId].CellTypeId;
                if (firstType == secondType)
                {
                    homotypicCounts[firstType]++;
                    sameTypeNeighbours[firstCellId].Add(secondCellId);
                    sameTypeNeighbours[secondCellId].Add(firstCellId);
                }
                else
                {
                    heterotypicCount++;
                }
            }
        }

        int typeACount = 0;
        int typeBCount = 0;
        int minimumArea = int.MaxValue;
        int maximumArea = 0;
        int minimumPerimeter = int.MaxValue;
        int maximumPerimeter = 0;
        long totalArea = 0;
        long totalPerimeter = 0;
        int biologicalCellCount = 0;
        for (int cellId = 1; cellId < present.Length; cellId++)
        {
            if (!present[cellId])
            {
                continue;
            }

            CellState cell = cellStates[cellId];
            biologicalCellCount++;
            if (cell.CellTypeId == typeAId)
            {
                typeACount++;
            }
            else if (cell.CellTypeId == typeBId)
            {
                typeBCount++;
            }

            minimumArea = Math.Min(minimumArea, cell.Area);
            maximumArea = Math.Max(maximumArea, cell.Area);
            minimumPerimeter = Math.Min(minimumPerimeter, cell.Perimeter);
            maximumPerimeter = Math.Max(maximumPerimeter, cell.Perimeter);
            totalArea += cell.Area;
            totalPerimeter += cell.Perimeter;
        }

        List<TypeInterfaceCount> homotypic = [];
        List<TypeDomainCount> domains = [];
        for (int typeId = 1; typeId < state.ContactEnergies.TypeCount; typeId++)
        {
            homotypic.Add(new TypeInterfaceCount(typeId, homotypicCounts[typeId]));
            (int domainCount, int largestDomain) = CountDomains(typeId, present, cellStates, sameTypeNeighbours);
            domains.Add(new TypeDomainCount(typeId, domainCount, largestDomain));
        }

        return new TissueMeasurements(
            heterotypicCount,
            totalCellCellCount,
            totalCellCellCount == 0 ? 0 : (double)heterotypicCount / totalCellCellCount,
            homotypic.ToArray(),
            typeACount,
            typeBCount,
            biologicalCellCount == 0 ? 0 : (double)totalArea / biologicalCellCount,
            biologicalCellCount == 0 ? 0 : minimumArea,
            maximumArea,
            biologicalCellCount == 0 ? 0 : (double)totalPerimeter / biologicalCellCount,
            biologicalCellCount == 0 ? 0 : minimumPerimeter,
            maximumPerimeter,
            domains.ToArray());
    }

    private static (int Count, int Largest) CountDomains(
        int typeId,
        bool[] present,
        CellState[] cells,
        List<int>[] neighbours)
    {
        bool[] visited = new bool[present.Length];
        Queue<int> queue = new();
        int domainCount = 0;
        int largestDomain = 0;

        for (int cellId = 1; cellId < present.Length; cellId++)
        {
            if (!present[cellId] || visited[cellId] || cells[cellId].CellTypeId != typeId)
            {
                continue;
            }

            domainCount++;
            int domainSize = 0;
            visited[cellId] = true;
            queue.Enqueue(cellId);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                domainSize++;
                foreach (int neighbour in neighbours[current])
                {
                    if (!visited[neighbour] && cells[neighbour].CellTypeId == typeId)
                    {
                        visited[neighbour] = true;
                        queue.Enqueue(neighbour);
                    }
                }
            }

            largestDomain = Math.Max(largestDomain, domainSize);
        }

        return (domainCount, largestDomain);
    }
}
