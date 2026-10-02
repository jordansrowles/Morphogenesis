using Rowles.Morphogenesis.Dynamics;
// Opt-in diagnostic copy. Differential tests require exact parity with the canonical kernel.
using System.Diagnostics;
using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Benchmarks.Diagnostics;

public static class ProfiledEnergyDeltaCalculator
{
    public static MoveEvaluation Evaluate(MorphogenesisState state, int targetIndex, int newCellId, ProposalStageProfile profile)
    {
        ArgumentNullException.ThrowIfNull(state);
        if ((uint)targetIndex >= (uint)state.SiteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(targetIndex));
        }

        if (newCellId < 0 || newCellId >= state.Cells.Length || (newCellId > 0 && !state.Cells[newCellId].IsAlive))
        {
            throw new ArgumentOutOfRangeException(nameof(newCellId));
        }

        int oldCellId = state.Lattice[targetIndex];
        if (oldCellId == newCellId)
        {
            return new MoveEvaluation(new HamiltonianBreakdown(0, 0, 0), 0, 0);
        }

        long stageStart = Stopwatch.GetTimestamp();
        double contact = ContactDelta(state, targetIndex, oldCellId, newCellId);
        profile.Record(ProposalStage.ContactEnergy, stageStart);
        stageStart = Stopwatch.GetTimestamp();
        double area = AreaDelta(state, oldCellId, newCellId);
        profile.Record(ProposalStage.AreaEnergy, stageStart);
        stageStart = Stopwatch.GetTimestamp();
        int oldPerimeterDelta = oldCellId > 0 ? PerimeterDelta(state, targetIndex, oldCellId, oldCellId, newCellId) : 0;
        int newPerimeterDelta = newCellId > 0 ? PerimeterDelta(state, targetIndex, newCellId, oldCellId, newCellId) : 0;
        double perimeter = PerimeterEnergyDelta(state, oldCellId, newCellId, oldPerimeterDelta, newPerimeterDelta);
        profile.Record(ProposalStage.PerimeterEnergy, stageStart);
        return new MoveEvaluation(
            new HamiltonianBreakdown(contact, area, perimeter),
            oldPerimeterDelta,
            newPerimeterDelta);
    }

    public static double ContactDelta(MorphogenesisState state, int targetIndex, int oldCellId, int newCellId)
    {
        double delta = 0;
        int oldType = state.CellTypeId(oldCellId);
        int newType = state.CellTypeId(newCellId);
        KernelPlan plan = state.KernelPlan;
        ReadOnlySpan<NeighbourOffset> offsets = plan.ContactOffsets;
        ReadOnlySpan<double> contacts = plan.ContactValues;
        int typeCount = plan.ContactTypeCount;
        int oldStride = oldType * typeCount;
        int newStride = newType * typeCount;
        for (int offsetIndex = 0; offsetIndex < offsets.Length; offsetIndex++)
        {
            NeighbourOffset offset = offsets[offsetIndex];
            int neighbourIndex = state.Resolve(targetIndex, offset.Dx, offset.Dy);
            if (neighbourIndex < 0)
            {
                delta -= contacts[oldStride];
                delta += contacts[newStride];
                continue;
            }

            int neighbourId = state.Lattice[neighbourIndex];
            int neighbourType = state.CellTypeId(neighbourId);
            if (oldCellId != neighbourId)
            {
                delta -= contacts[oldStride + neighbourType];
            }

            if (newCellId != neighbourId)
            {
                delta += contacts[newStride + neighbourType];
            }
        }

        return delta;
    }

    public static double AreaDelta(MorphogenesisState state, int oldCellId, int newCellId)
    {
        double delta = 0;
        if (newCellId > 0)
        {
            CellRuntime gaining = state.Cells[newCellId];
            double before = gaining.Area - gaining.TargetArea;
            double after = gaining.Area + 1 - gaining.TargetArea;
            delta += gaining.AreaStiffness * (after * after - before * before);
        }

        if (oldCellId > 0)
        {
            CellRuntime losing = state.Cells[oldCellId];
            double before = losing.Area - losing.TargetArea;
            double after = losing.Area - 1 - losing.TargetArea;
            delta += losing.AreaStiffness * (after * after - before * before);
        }

        return delta;
    }

    public static int PerimeterDelta(MorphogenesisState state, int targetIndex, int cellId, int oldCellId, int newCellId)
    {
        int delta = 0;
        ReadOnlySpan<NeighbourOffset> offsets = state.KernelPlan.PerimeterOffsets;
        for (int offsetIndex = 0; offsetIndex < offsets.Length; offsetIndex++)
        {
            NeighbourOffset offset = offsets[offsetIndex];
            int neighbourIndex = state.Resolve(targetIndex, offset.Dx, offset.Dy);
            if (oldCellId == cellId)
            {
                bool wasUnlike = neighbourIndex < 0 || state.Lattice[neighbourIndex] != oldCellId;
                if (wasUnlike)
                {
                    delta--;
                }
            }

            if (newCellId == cellId)
            {
                bool isUnlike = neighbourIndex < 0 || state.Lattice[neighbourIndex] != newCellId;
                if (isUnlike)
                {
                    delta++;
                }
            }

            if (neighbourIndex >= 0 && state.Lattice[neighbourIndex] == cellId)
            {
                bool wasUnlike = oldCellId != cellId;
                bool isUnlike = newCellId != cellId;
                if (wasUnlike != isUnlike)
                {
                    delta += isUnlike ? 1 : -1;
                }
            }
        }

        return delta;
    }

    private static double PerimeterEnergyDelta(
        MorphogenesisState state,
        int oldCellId,
        int newCellId,
        int oldPerimeterDelta,
        int newPerimeterDelta)
    {
        double delta = 0;
        if (oldCellId > 0)
        {
            CellRuntime losing = state.Cells[oldCellId];
            double before = losing.Perimeter - losing.TargetPerimeter;
            double after = losing.Perimeter + oldPerimeterDelta - losing.TargetPerimeter;
            delta += losing.PerimeterStiffness * (after * after - before * before);
        }

        if (newCellId > 0)
        {
            CellRuntime gaining = state.Cells[newCellId];
            double before = gaining.Perimeter - gaining.TargetPerimeter;
            double after = gaining.Perimeter + newPerimeterDelta - gaining.TargetPerimeter;
            delta += gaining.PerimeterStiffness * (after * after - before * before);
        }

        return delta;
    }
}
