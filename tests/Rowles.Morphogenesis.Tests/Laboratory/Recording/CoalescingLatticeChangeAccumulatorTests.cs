using Rowles.Morphogenesis.Laboratory.Recording;

namespace Rowles.Morphogenesis.Tests.Laboratory.Recording;

public sealed class CoalescingLatticeChangeAccumulatorTests
{
    [Fact]
    public void OneAcceptedCopyIsExposedAndReset()
    {
        CoalescingLatticeChangeAccumulator accumulator = new(siteCount: 8);
        int[] cellIds = new int[8];
        cellIds[5] = 2;

        accumulator.AcceptedCopy(targetIndex: 5, oldCellId: 0, newCellId: 2);
        accumulator.SortChanges(cellIds);

        Assert.Equal(1, accumulator.Count);
        Assert.Equal([5], accumulator.SortedIndices.ToArray());
        Assert.Equal([2], accumulator.SortedCellIds.ToArray());
        accumulator.Reset();
        Assert.Equal(0, accumulator.Count);
        cellIds[5] = 0;
        accumulator.AcceptedCopy(targetIndex: 5, oldCellId: 2, newCellId: 0);
        accumulator.SortChanges(cellIds);
        Assert.Equal([0], accumulator.SortedCellIds.ToArray());
    }

    [Fact]
    public void RepeatedChangesRetainOneSiteAndTheLatestCellId()
    {
        CoalescingLatticeChangeAccumulator accumulator = new(siteCount: 16);

        accumulator.AcceptedCopy(targetIndex: 7, oldCellId: 0, newCellId: 1);
        accumulator.AcceptedCopy(targetIndex: 7, oldCellId: 1, newCellId: 3);
        accumulator.AcceptedCopy(targetIndex: 7, oldCellId: 3, newCellId: 2);
        int[] cellIds = new int[16];
        cellIds[7] = 2;
        accumulator.SortChanges(cellIds);

        Assert.Equal(1, accumulator.Count);
        Assert.Equal([7], accumulator.SortedIndices.ToArray());
        Assert.Equal([2], accumulator.SortedCellIds.ToArray());
    }

    [Fact]
    public void ManyUniqueChangesAreSortedAlongsideTheirCellIds()
    {
        CoalescingLatticeChangeAccumulator accumulator = new(siteCount: 64);
        int[] cellIds = new int[64];
        for (int index = 63; index >= 0; index--)
        {
            cellIds[index] = index % 5;
            accumulator.AcceptedCopy(index, oldCellId: 0, newCellId: cellIds[index]);
        }

        accumulator.SortChanges(cellIds);
        Assert.Equal(64, accumulator.Count);
        Assert.Equal(Enumerable.Range(0, 64), accumulator.SortedIndices.ToArray());
        Assert.Equal(Enumerable.Range(0, 64).Select(index => index % 5), accumulator.SortedCellIds.ToArray());
        accumulator.Reset();
        Assert.Equal(0, accumulator.Count);
    }

    [Fact]
    public void AcceptedCopyDoesNotAllocateAfterConstruction()
    {
        CoalescingLatticeChangeAccumulator accumulator = new(siteCount: 1024);
        for (int index = 0; index < 1024; index++)
        {
            accumulator.AcceptedCopy(index, oldCellId: 0, newCellId: 1);
        }

        accumulator.Reset();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 10_000; iteration++)
        {
            accumulator.AcceptedCopy(iteration % 1024, oldCellId: 0, newCellId: iteration & 7);
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
        Assert.Equal(1024, accumulator.Count);
    }
}
