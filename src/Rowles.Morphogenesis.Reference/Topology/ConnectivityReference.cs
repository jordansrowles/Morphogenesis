using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Model;

namespace Rowles.Morphogenesis.Reference.Topology;

public static class ConnectivityReference
{
    public static int CountComponents(ReferenceState state, int cellId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (cellId < 0 || (cellId > 0 && !state.CellsById.ContainsKey(cellId)))
        {
            throw new ArgumentOutOfRangeException(nameof(cellId));
        }

        bool[,] visited = new bool[state.Width, state.Height];
        Queue<GridPoint> pending = new();
        int components = 0;
        ReadOnlySpan<GridPoint> offsets = state.Conventions.ConnectivityAdjacency.Offsets;

        for (int y = 0; y < state.Height; y++)
        {
            for (int x = 0; x < state.Width; x++)
            {
                if (visited[x, y] || state.CellIdAt(x, y) != cellId)
                {
                    continue;
                }

                components++;
                visited[x, y] = true;
                pending.Enqueue(new GridPoint(x, y));
                while (pending.TryDequeue(out GridPoint current))
                {
                    foreach (GridPoint offset in offsets)
                    {
                        GridPoint neighbour = PeriodicLattice.Resolve(current, offset, state.Width, state.Height);
                        if (!visited[neighbour.X, neighbour.Y] && state.CellIdAt(neighbour) == cellId)
                        {
                            visited[neighbour.X, neighbour.Y] = true;
                            pending.Enqueue(neighbour);
                        }
                    }
                }
            }
        }

        return components;
    }

    public static bool IsConnected(ReferenceState state, int cellId) => CountComponents(state, cellId) == 1;

    public static ProposedConnectivityCheck EvaluateCopy(ReferenceState state, GridPoint source, GridPoint target)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.ValidatePoint(source);
        state.ValidatePoint(target);
        if (!state.AreCopyNeighbours(source, target))
        {
            throw new ArgumentException($"Source {source} must be in the copy neighbourhood of target {target}.", nameof(source));
        }

        int losingCellId = state.CellIdAt(target);
        if (losingCellId == 0)
        {
            return new ProposedConnectivityCheck(0, null);
        }

        ReferenceState changed = state.Clone();
        changed.SetCellId(target, state.CellIdAt(source));
        int componentsAfter = CountComponents(changed, losingCellId);
        return new ProposedConnectivityCheck(losingCellId, componentsAfter);
    }

    public static bool WouldRemainConnectedAfterCopy(ReferenceState state, GridPoint source, GridPoint target) =>
        EvaluateCopy(state, source, target).WouldRemainConnected;
}
