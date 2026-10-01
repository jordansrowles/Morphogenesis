using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Topology;

namespace Rowles.Morphogenesis.Diagnostics;

public static class InvariantValidator
{
    public static void Validate(MorphogenesisState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        int[] areas = new int[state.Cells.Length];
        int[] perimeters = new int[state.Cells.Length];
        for (int index = 0; index < state.SiteCount; index++)
        {
            int cellId = state.Lattice[index];
            if (cellId < 0 || cellId >= state.Cells.Length || (cellId > 0 && !state.Cells[cellId].IsAlive))
            {
                throw new InvalidOperationException($"Lattice site {index} refers to an unknown or inactive cell ID {cellId}.");
            }

            if (cellId == 0)
            {
                continue;
            }

            areas[cellId]++;
            for (int offsetIndex = 0; offsetIndex < StencilGeometry.Count(state.Configuration.Conventions.PerimeterNeighbourhood); offsetIndex++)
            {
                StencilGeometry.PerimeterOffset(state.Configuration.Conventions.PerimeterNeighbourhood, offsetIndex, out int dx, out int dy);
                int neighbour = state.Resolve(index, dx, dy);
                if (neighbour < 0 || state.Lattice[neighbour] != cellId)
                {
                    perimeters[cellId]++;
                }
            }
        }

        int[] componentCounts = CellConnectivity.CountComponentsByCell(state);
        for (int cellId = 1; cellId < state.Cells.Length; cellId++)
        {
            CellRuntime cell = state.Cells[cellId];
            if (!cell.IsAlive)
            {
                if (areas[cellId] != 0)
                {
                    throw new InvalidOperationException($"Inactive cell ID {cellId} still occupies lattice sites.");
                }

                continue;
            }

            if (cell.Area == 0 || areas[cellId] != cell.Area)
            {
                throw new InvalidOperationException($"Cell {cellId} area differs from the exact lattice count.");
            }

            if (perimeters[cellId] != cell.Perimeter)
            {
                throw new InvalidOperationException($"Cell {cellId} perimeter differs from exact recomputation.");
            }

            if ((uint)cell.CellTypeId >= (uint)state.ContactEnergies.TypeCount)
            {
                throw new InvalidOperationException($"Cell {cellId} refers to an invalid contact type.");
            }

            if (componentCounts[cellId] != 1)
            {
                throw new InvalidOperationException($"Cell {cellId} is disconnected under the configured adjacency.");
            }
        }
    }

    public static HamiltonianBreakdown RecomputeHamiltonian(MorphogenesisState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        double contact = 0;
        double areaEnergy = 0;
        double perimeterEnergy = 0;
        int[] areas = new int[state.Cells.Length];
        int[] perimeters = new int[state.Cells.Length];
        ContactCouplingNeighbourhood contactNeighbourhood = state.Configuration.Conventions.ContactCouplingNeighbourhood;
        PerimeterNeighbourhood perimeterNeighbourhood = state.Configuration.Conventions.PerimeterNeighbourhood;

        for (int index = 0; index < state.SiteCount; index++)
        {
            int cellId = state.Lattice[index];
            if (cellId > 0)
            {
                areas[cellId]++;
                for (int offsetIndex = 0; offsetIndex < StencilGeometry.Count(perimeterNeighbourhood); offsetIndex++)
                {
                    StencilGeometry.PerimeterOffset(perimeterNeighbourhood, offsetIndex, out int dx, out int dy);
                    int neighbour = state.Resolve(index, dx, dy);
                    if (neighbour < 0 || state.Lattice[neighbour] != cellId)
                    {
                        perimeters[cellId]++;
                    }
                }
            }

            int cellType = state.CellTypeId(cellId);
            for (int offsetIndex = 0; offsetIndex < StencilGeometry.Count(contactNeighbourhood); offsetIndex++)
            {
                StencilGeometry.ContactOffset(contactNeighbourhood, offsetIndex, out int dx, out int dy);
                int neighbour = state.Resolve(index, dx, dy);
                if (neighbour < 0)
                {
                    contact += state.ContactEnergies[cellType, 0];
                    continue;
                }

                if (!StencilGeometry.IsPositiveHalf(dx, dy))
                {
                    continue;
                }

                int neighbourId = state.Lattice[neighbour];
                if (cellId != neighbourId)
                {
                    contact += state.ContactEnergies[cellType, state.CellTypeId(neighbourId)];
                }
            }
        }

        for (int cellId = 1; cellId < state.Cells.Length; cellId++)
        {
            CellRuntime cell = state.Cells[cellId];
            if (!cell.IsAlive)
            {
                continue;
            }

            double areaDifference = areas[cellId] - cell.TargetArea;
            double perimeterDifference = perimeters[cellId] - cell.TargetPerimeter;
            areaEnergy += cell.AreaStiffness * areaDifference * areaDifference;
            perimeterEnergy += cell.PerimeterStiffness * perimeterDifference * perimeterDifference;
        }

        return new HamiltonianBreakdown(contact, areaEnergy, perimeterEnergy);
    }
}
