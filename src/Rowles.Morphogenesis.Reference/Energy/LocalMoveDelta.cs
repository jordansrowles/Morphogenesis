using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Model;

namespace Rowles.Morphogenesis.Reference.Energy;

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
