using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Model;

namespace Rowles.Morphogenesis.Reference.Energy;

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
