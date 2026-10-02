// Opt-in diagnostic copy. Differential tests require exact parity with the canonical kernel.
using System.Diagnostics;
using Rowles.Morphogenesis.Topology;
using System.Numerics;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Benchmarks.Diagnostics;

public static class ProfiledCellConnectivity
{
    private const uint CardinalRingBits = (1u << 1) | (1u << 3) | (1u << 4) | (1u << 6);

    internal static int[] CountComponentsByCell(MorphogenesisState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        int[] componentCounts = new int[state.Cells.Length];
        bool[] visited = new bool[state.SiteCount];
        int[] queue = new int[state.SiteCount];
        for (int index = 0; index < state.SiteCount; index++)
        {
            int cellId = state.Lattice[index];
            if (cellId == 0 || visited[index])
            {
                continue;
            }

            componentCounts[cellId]++;
            int head = 0;
            int tail = 0;
            queue[tail++] = index;
            visited[index] = true;
            while (head < tail)
            {
                int current = queue[head++];
                for (int offsetIndex = 0; offsetIndex < StencilGeometry.Count(state.Configuration.Conventions.ConnectivityAdjacency); offsetIndex++)
                {
                    StencilGeometry.ConnectivityOffset(state.Configuration.Conventions.ConnectivityAdjacency, offsetIndex, out int dx, out int dy);
                    int neighbour = state.Resolve(current, dx, dy);
                    if (neighbour >= 0 && !visited[neighbour] && state.Lattice[neighbour] == cellId)
                    {
                        visited[neighbour] = true;
                        queue[tail++] = neighbour;
                    }
                }
            }
        }

        return componentCounts;
    }

    public static int CountComponents(MorphogenesisState state, int cellId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (cellId <= 0 || cellId >= state.Cells.Length || !state.Cells[cellId].IsAlive)
        {
            throw new ArgumentOutOfRangeException(nameof(cellId));
        }

        bool[] visited = new bool[state.SiteCount];
        int[] queue = new int[state.SiteCount];
        int componentCount = 0;
        for (int index = 0; index < state.SiteCount; index++)
        {
            if (visited[index] || state.Lattice[index] != cellId)
            {
                continue;
            }

            componentCount++;
            int head = 0;
            int tail = 0;
            queue[tail++] = index;
            visited[index] = true;
            while (head < tail)
            {
                int current = queue[head++];
                for (int offsetIndex = 0; offsetIndex < StencilGeometry.Count(state.Configuration.Conventions.ConnectivityAdjacency); offsetIndex++)
                {
                    StencilGeometry.ConnectivityOffset(state.Configuration.Conventions.ConnectivityAdjacency, offsetIndex, out int dx, out int dy);
                    int neighbour = state.Resolve(current, dx, dy);
                    if (neighbour >= 0 && !visited[neighbour] && state.Lattice[neighbour] == cellId)
                    {
                        visited[neighbour] = true;
                        queue[tail++] = neighbour;
                    }
                }
            }
        }

        return componentCount;
    }

    public static ConnectivityEvaluation EvaluateRemoval(
        MorphogenesisState state,
        int targetIndex,
        ConnectivityWorkspace workspace, ProposalStageProfile profile)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workspace);
        if ((uint)targetIndex >= (uint)state.SiteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(targetIndex));
        }

        int cellId = state.Lattice[targetIndex];
        if (cellId <= 0 || !state.Cells[cellId].IsAlive)
        {
            throw new ArgumentException("The target must belong to a live biological cell.", nameof(targetIndex));
        }

        if (workspace.Queue.Length < state.SiteCount || workspace.VisitedAt.Length < state.SiteCount)
        {
            throw new ArgumentException("The connectivity workspace is smaller than the lattice.", nameof(workspace));
        }

        int remainingSites = state.Cells[cellId].Area - 1;
        if (remainingSites == 0)
        {
            return new ConnectivityEvaluation(false, false, 0);
        }

        long stageStart = Stopwatch.GetTimestamp();
        uint localCellMask = LocalMooreCellMask(state, targetIndex, cellId);
        uint remainingCardinal = localCellMask & CardinalRingBits;
        if (BitOperations.PopCount(remainingCardinal) <= 1 || RingConnects(localCellMask, remainingCardinal))
        {
            profile.Record(ProposalStage.ConnectivityLocal, stageStart);
            return new ConnectivityEvaluation(true, false, remainingSites);
        }

        profile.Record(ProposalStage.ConnectivityLocal, stageStart);
        stageStart = Stopwatch.GetTimestamp();
        bool connected = FloodFillAfterRemoval(state, targetIndex, cellId, remainingSites, workspace);
        profile.Record(ProposalStage.ConnectivityFallback, stageStart);
        return new ConnectivityEvaluation(connected, true, remainingSites);
    }

    private static uint LocalMooreCellMask(MorphogenesisState state, int targetIndex, int cellId)
    {
        uint mask = 0;
        for (int index = 0; index < StencilGeometry.MooreCount; index++)
        {
            StencilGeometry.MooreOffset(index, out int dx, out int dy);
            int neighbour = state.Resolve(targetIndex, dx, dy);
            if (neighbour >= 0 && state.Lattice[neighbour] == cellId)
            {
                mask |= 1u << index;
            }
        }

        return mask;
    }

    private static bool RingConnects(uint occupied, uint cardinal)
    {
        if (cardinal == 0)
        {
            return true;
        }

        int first = BitOperations.TrailingZeroCount(cardinal);
        uint reached = 1u << first;
        uint frontier = reached;
        while (frontier != 0)
        {
            uint expanded = 0;
            while (frontier != 0)
            {
                int current = BitOperations.TrailingZeroCount(frontier);
                frontier &= frontier - 1;
                expanded |= RingNeighbours(current);
            }

            frontier = expanded & occupied & ~reached;
            reached |= frontier;
        }

        return (reached & cardinal) == cardinal;
    }

    private static uint RingNeighbours(int index) => index switch
    {
        0 => (1u << 1) | (1u << 3),
        1 => (1u << 0) | (1u << 2),
        2 => (1u << 1) | (1u << 4),
        3 => (1u << 0) | (1u << 5),
        4 => (1u << 2) | (1u << 7),
        5 => (1u << 3) | (1u << 6),
        6 => (1u << 5) | (1u << 7),
        7 => (1u << 4) | (1u << 6),
        _ => 0
    };

    private static bool FloodFillAfterRemoval(
        MorphogenesisState state,
        int targetIndex,
        int cellId,
        int expectedCount,
        ConnectivityWorkspace workspace)
    {
        int first = -1;
        for (int offsetIndex = 0; offsetIndex < StencilGeometry.Count(state.Configuration.Conventions.ConnectivityAdjacency); offsetIndex++)
        {
            StencilGeometry.ConnectivityOffset(state.Configuration.Conventions.ConnectivityAdjacency, offsetIndex, out int dx, out int dy);
            int neighbour = state.Resolve(targetIndex, dx, dy);
            if (neighbour >= 0 && state.Lattice[neighbour] == cellId)
            {
                first = neighbour;
                break;
            }
        }

        if (first < 0)
        {
            return false;
        }

        if (workspace.Generation == int.MaxValue)
        {
            Array.Clear(workspace.VisitedAt);
            workspace.Generation = 1;
        }
        else
        {
            workspace.Generation++;
            if (workspace.Generation == 0)
            {
                workspace.Generation = 1;
            }
        }

        int generation = workspace.Generation;
        int head = 0;
        int tail = 0;
        workspace.Queue[tail++] = first;
        workspace.VisitedAt[first] = generation;
        while (head < tail)
        {
            int current = workspace.Queue[head++];
            for (int offsetIndex = 0; offsetIndex < StencilGeometry.Count(state.Configuration.Conventions.ConnectivityAdjacency); offsetIndex++)
            {
                StencilGeometry.ConnectivityOffset(state.Configuration.Conventions.ConnectivityAdjacency, offsetIndex, out int dx, out int dy);
                int neighbour = state.Resolve(current, dx, dy);
                if (neighbour < 0 || neighbour == targetIndex ||
                    workspace.VisitedAt[neighbour] == generation || state.Lattice[neighbour] != cellId)
                {
                    continue;
                }

                workspace.VisitedAt[neighbour] = generation;
                workspace.Queue[tail++] = neighbour;
            }
        }

        return tail == expectedCount;
    }
}
