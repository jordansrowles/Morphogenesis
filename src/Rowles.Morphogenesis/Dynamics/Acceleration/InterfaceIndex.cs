using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Dynamics.Acceleration;

/// <summary>
/// Incremental proposal-space index. Wall directions are included because canonical attempts
/// reject them; only same-ID proposals are excluded. Direction identities retain multiplicity.
/// </summary>
internal sealed class InterfaceIndex
{
    private readonly MorphogenesisState _state;
    internal DenseIndexSet BorderSites { get; }
    internal DenseIndexSet DirectedProposals { get; }
    internal int Degree { get; }

    internal InterfaceIndex(MorphogenesisState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        Degree = StencilGeometry.Count(state.Configuration.Conventions.CopyNeighbourhood);
        BorderSites = new DenseIndexSet(state.SiteCount);
        DirectedProposals = new DenseIndexSet(checked(state.SiteCount * Degree));
        for (int target = 0; target < state.SiteCount; target++) Refresh(target);
    }

    internal void UpdateAfterAcceptedCopy(int target)
    {
        if ((uint)target >= (uint)_state.SiteCount) throw new ArgumentOutOfRangeException(nameof(target));
        Refresh(target);
        for (int direction = 0; direction < Degree; direction++)
        {
            int neighbour = Resolve(target, direction);
            if (neighbour >= 0) Refresh(neighbour);
        }
    }

    internal int Resolve(int target, int direction)
    {
        StencilGeometry.CopyOffset(_state.Configuration.Conventions.CopyNeighbourhood, direction, out int dx, out int dy);
        return _state.Resolve(target, dx, dy);
    }

    private void Refresh(int target)
    {
        bool border = false;
        for (int direction = 0; direction < Degree; direction++)
        {
            int source = Resolve(target, direction);
            int edge = target * Degree + direction;
            bool eligible = source < 0 || _state.Lattice[target] != _state.Lattice[source];
            if (eligible)
            {
                DirectedProposals.Add(edge);
                border = true;
            }
            else DirectedProposals.Remove(edge);
        }
        if (border) BorderSites.Add(target);
        else BorderSites.Remove(target);
    }
}
