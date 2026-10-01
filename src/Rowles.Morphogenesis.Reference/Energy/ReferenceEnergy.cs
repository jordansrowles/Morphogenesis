using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Model;

namespace Rowles.Morphogenesis.Reference.Energy;

public static class ReferenceEnergy
{
    public static int ComputeArea(ReferenceState state, int cellId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (cellId < 0 || (cellId > 0 && !state.CellsById.ContainsKey(cellId)))
        {
            throw new ArgumentOutOfRangeException(nameof(cellId));
        }

        int area = 0;
        for (int x = 0; x < state.Width; x++)
        {
            for (int y = 0; y < state.Height; y++)
            {
                if (state.CellIdAt(x, y) == cellId)
                {
                    area++;
                }
            }
        }

        return area;
    }

    public static int ComputePerimeter(ReferenceState state, int cellId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (cellId <= 0 || !state.CellsById.ContainsKey(cellId))
        {
            throw new ArgumentOutOfRangeException(nameof(cellId), "Perimeter is defined here for biological cells only.");
        }

        int perimeter = 0;
        ReadOnlySpan<GridPoint> offsets = state.Conventions.PerimeterNeighbourhood.Offsets;
        for (int x = 0; x < state.Width; x++)
        {
            for (int y = 0; y < state.Height; y++)
            {
                if (state.CellIdAt(x, y) != cellId)
                {
                    continue;
                }

                GridPoint site = new(x, y);
                foreach (GridPoint offset in offsets)
                {
                    GridPoint neighbour = PeriodicLattice.Resolve(site, offset, state.Width, state.Height);
                    if (state.CellIdAt(neighbour) != cellId)
                    {
                        perimeter++;
                    }
                }
            }
        }

        return perimeter;
    }

    public static double ComputeContactEnergy(
        ReferenceState state,
        ContactCouplingNeighbourhood? couplingNeighbourhood = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ContactCouplingNeighbourhood coupling = couplingNeighbourhood ?? state.Conventions.ContactCouplingNeighbourhood;
        ValidateNeighbourhoodForState(coupling.Offsets, state);

        double contact = 0;
        foreach (GridPoint site in AllSites(state))
        {
            int idA = state.CellIdAt(site);
            int typeA = state.CellTypeIdForCell(idA);
            foreach (GridPoint offset in coupling.PositiveHalf)
            {
                GridPoint neighbour = PeriodicLattice.Resolve(site, offset, state.Width, state.Height);
                int idB = state.CellIdAt(neighbour);
                if (idA != idB)
                {
                    contact += state.ContactEnergies[typeA, state.CellTypeIdForCell(idB)];
                }
            }
        }

        return contact;
    }

    public static HamiltonianBreakdown ComputeHamiltonian(ReferenceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        double contact = ComputeContactEnergy(state);
        double areaEnergy = 0;
        double perimeterEnergy = 0;
        foreach (CellDefinition cell in state.CellsById.Values)
        {
            int area = ComputeArea(state, cell.CellId);
            int perimeter = ComputePerimeter(state, cell.CellId);
            double areaDifference = area - cell.TargetArea;
            double perimeterDifference = perimeter - cell.TargetPerimeter;
            areaEnergy += cell.AreaStiffness * areaDifference * areaDifference;
            perimeterEnergy += cell.PerimeterStiffness * perimeterDifference * perimeterDifference;
        }

        return new HamiltonianBreakdown(contact, areaEnergy, perimeterEnergy);
    }

    private static IEnumerable<GridPoint> AllSites(ReferenceState state)
    {
        for (int y = 0; y < state.Height; y++)
        {
            for (int x = 0; x < state.Width; x++)
            {
                yield return new GridPoint(x, y);
            }
        }
    }

    private static void ValidateNeighbourhoodForState(ReadOnlySpan<GridPoint> offsets, ReferenceState state)
    {
        HashSet<int> neighbours = [];
        foreach (GridPoint offset in offsets)
        {
            int x = PeriodicLattice.Wrap(offset.X, state.Width);
            int y = PeriodicLattice.Wrap(offset.Y, state.Height);
            int index = y * state.Width + x;
            if (index == 0 || !neighbours.Add(index))
            {
                throw new ArgumentException("The coupling stencil aliases sites on this periodic lattice.", nameof(state));
            }
        }
    }
}
