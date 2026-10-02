using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Tests.Fixtures;

namespace Rowles.Morphogenesis.Tests.Model;

public sealed class MorphogenesisStateReadoutTests
{
    private static readonly double[,] Contacts =
    {
        { 0, 1, 1 },
        { 1, 0, 1 },
        { 1, 1, 0 }
    };

    [Fact]
    public void Copy_cell_ids_to_copies_only_the_lattice_and_keeps_the_remainder_untouched()
    {
        MorphogenesisState state = CreateState();
        int[] expected = state.GetCellIdsCopy();
        int[] destination = Enumerable.Repeat(-123, state.SiteCount + 3).ToArray();

        state.CopyCellIdsTo(destination);

        Assert.Equal(expected, destination[..state.SiteCount]);
        Assert.All(destination[state.SiteCount..], value => Assert.Equal(-123, value));
        Assert.Contains(0, destination[..state.SiteCount]);
        Assert.Contains(1, destination[..state.SiteCount]);
        Assert.Contains(3, destination[..state.SiteCount]);
        Assert.Throws<ArgumentException>(() => state.CopyCellIdsTo(new int[state.SiteCount - 1]));
    }

    [Fact]
    public void Copy_cell_ids_to_allocates_nothing_with_a_caller_owned_buffer()
    {
        MorphogenesisState state = CreateState();
        int[] destination = new int[state.SiteCount];
        for (int index = 0; index < 20; index++)
        {
            state.CopyCellIdsTo(destination);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 10_000; index++)
        {
            state.CopyCellIdsTo(destination);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(state.GetCellIdsCopy(), destination);
    }

    [Fact]
    public void Cell_capacity_and_try_get_cell_state_report_only_live_biological_cells()
    {
        MorphogenesisState state = CreateState();

        Assert.Equal(4, state.CellCapacity);
        Assert.False(state.TryGetCellState(0, out CellState medium));
        Assert.Equal(default, medium);
        Assert.False(state.TryGetCellState(-1, out CellState negative));
        Assert.Equal(default, negative);
        Assert.False(state.TryGetCellState(state.CellCapacity, out CellState outOfRange));
        Assert.Equal(default, outOfRange);
        Assert.False(state.TryGetCellState(2, out CellState nonLive));
        Assert.Equal(default, nonLive);

        Assert.True(state.TryGetCellState(1, out CellState first));
        Assert.Equal(state.GetCellState(1), first);
        Assert.True(state.TryGetCellState(3, out CellState second));
        Assert.Equal(state.GetCellState(3), second);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.GetCellState(2));
        Assert.True(typeof(CellState).IsValueType);
    }

    [Fact]
    public void Repeated_live_cell_lookup_allocates_nothing()
    {
        MorphogenesisState state = CreateState();
        CellState result = default;
        for (int index = 0; index < 20; index++)
        {
            _ = state.TryGetCellState(1, out result);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 10_000; index++)
        {
            _ = state.TryGetCellState(1, out result);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(state.GetCellState(1), result);
    }

    private static MorphogenesisState CreateState() => ProductionTestStateFactory.FromRows(
        [
            "......",
            "......",
            ".AA...",
            ".AA.BB",
            ".....B",
            "......"
        ],
        [
            new CellDefinition(1, 1, 4, 0.5, 16, 0.1),
            new CellDefinition(3, 2, 3, 0.5, 14, 0.1)
        ],
        Contacts,
        SimulationConfiguration.WallCanonical);
}
