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

public readonly record struct MoveDelta(
    GridPoint Source,
    GridPoint Target,
    int OldCellId,
    int NewCellId,
    HamiltonianBreakdown Terms)
{
    public double Total => Terms.Total;
}

public readonly record struct BruteForceMove(
    GridPoint Source,
    GridPoint Target,
    int OldCellId,
    int NewCellId,
    HamiltonianBreakdown Before,
    HamiltonianBreakdown After)
{
    public HamiltonianBreakdown Terms => After - Before;

    public double TotalDelta => Terms.Total;
}

public static class BruteForceMoveReference
{
    public static BruteForceMove Evaluate(ReferenceState state, GridPoint source, GridPoint target)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.ValidatePoint(source);
        state.ValidatePoint(target);
        if (!state.AreCopyNeighbours(source, target))
        {
            throw new ArgumentException($"Source {source} must be in the copy neighbourhood of target {target}.", nameof(source));
        }

        int oldId = state.CellIdAt(target);
        int newId = state.CellIdAt(source);
        HamiltonianBreakdown before = ReferenceEnergy.ComputeHamiltonian(state);
        ReferenceState changed = state.Clone();
        changed.SetCellId(target, newId);
        HamiltonianBreakdown after = ReferenceEnergy.ComputeHamiltonian(changed);
        return new BruteForceMove(source, target, oldId, newId, before, after);
    }
}

public static class LocalMoveDelta
{
    public static MoveDelta Evaluate(ReferenceState state, GridPoint source, GridPoint target)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.ValidatePoint(source);
        state.ValidatePoint(target);
        if (!state.AreCopyNeighbours(source, target))
        {
            throw new ArgumentException($"Source {source} must be in the copy neighbourhood of target {target}.", nameof(source));
        }

        int oldId = state.CellIdAt(target);
        int newId = state.CellIdAt(source);
        if (oldId == newId)
        {
            return new MoveDelta(source, target, oldId, newId, new HamiltonianBreakdown(0, 0, 0));
        }

        double contact = ContactDelta(state, target, oldId, newId);
        double area = AreaDelta(state, oldId, newId);
        double perimeter = PerimeterDelta(state, target, oldId, newId);
        return new MoveDelta(source, target, oldId, newId, new HamiltonianBreakdown(contact, area, perimeter));
    }

    public static double ContactDelta(ReferenceState state, GridPoint target, int oldId, int newId)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.ValidatePoint(target);
        if (oldId == newId)
        {
            return 0;
        }

        int oldType = state.CellTypeIdForCell(oldId);
        int newType = state.CellTypeIdForCell(newId);
        double delta = 0;
        foreach (GridPoint offset in state.Conventions.ContactCouplingNeighbourhood.Offsets)
        {
            GridPoint neighbour = PeriodicLattice.Resolve(target, offset, state.Width, state.Height);
            int neighbourId = state.CellIdAt(neighbour);
            int neighbourType = state.CellTypeIdForCell(neighbourId);
            if (oldId != neighbourId)
            {
                delta -= state.ContactEnergies[oldType, neighbourType];
            }

            if (newId != neighbourId)
            {
                delta += state.ContactEnergies[newType, neighbourType];
            }
        }

        return delta;
    }

    public static double AreaDelta(ReferenceState state, int oldId, int newId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (oldId == newId)
        {
            return 0;
        }

        double delta = 0;
        if (newId > 0)
        {
            CellDefinition gainingCell = state.GetCellDefinition(newId);
            int currentArea = ReferenceEnergy.ComputeArea(state, newId);
            double beforeDifference = currentArea - gainingCell.TargetArea;
            double afterDifference = currentArea + 1 - gainingCell.TargetArea;
            delta += gainingCell.AreaStiffness *
                (afterDifference * afterDifference - beforeDifference * beforeDifference);
        }

        if (oldId > 0)
        {
            CellDefinition losingCell = state.GetCellDefinition(oldId);
            int currentArea = ReferenceEnergy.ComputeArea(state, oldId);
            double beforeDifference = currentArea - losingCell.TargetArea;
            double afterDifference = currentArea - 1 - losingCell.TargetArea;
            delta += losingCell.AreaStiffness *
                (afterDifference * afterDifference - beforeDifference * beforeDifference);
        }

        return delta;
    }

    public static double PerimeterDelta(ReferenceState state, GridPoint target, int oldId, int newId)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.ValidatePoint(target);
        if (oldId == newId)
        {
            return 0;
        }

        int neighboursDifferentFromOld = 0;
        int neighboursSameAsOld = 0;
        int neighboursDifferentFromNew = 0;
        int neighboursSameAsNew = 0;
        foreach (GridPoint offset in state.Conventions.PerimeterNeighbourhood.Offsets)
        {
            GridPoint neighbour = PeriodicLattice.Resolve(target, offset, state.Width, state.Height);
            int neighbourId = state.CellIdAt(neighbour);
            if (neighbourId != oldId)
            {
                neighboursDifferentFromOld++;
            }
            else
            {
                neighboursSameAsOld++;
            }

            if (neighbourId != newId)
            {
                neighboursDifferentFromNew++;
            }
            else
            {
                neighboursSameAsNew++;
            }
        }

        double delta = 0;
        if (oldId > 0)
        {
            CellDefinition losingCell = state.GetCellDefinition(oldId);
            int currentPerimeter = ReferenceEnergy.ComputePerimeter(state, oldId);
            int perimeterChange = -neighboursDifferentFromOld + neighboursSameAsOld;
            double beforeDifference = currentPerimeter - losingCell.TargetPerimeter;
            double afterDifference = currentPerimeter + perimeterChange - losingCell.TargetPerimeter;
            delta += losingCell.PerimeterStiffness *
                (afterDifference * afterDifference - beforeDifference * beforeDifference);
        }

        if (newId > 0)
        {
            CellDefinition gainingCell = state.GetCellDefinition(newId);
            int currentPerimeter = ReferenceEnergy.ComputePerimeter(state, newId);
            int perimeterChange = neighboursDifferentFromNew - neighboursSameAsNew;
            double beforeDifference = currentPerimeter - gainingCell.TargetPerimeter;
            double afterDifference = currentPerimeter + perimeterChange - gainingCell.TargetPerimeter;
            delta += gainingCell.PerimeterStiffness *
                (afterDifference * afterDifference - beforeDifference * beforeDifference);
        }

        return delta;
    }
}
