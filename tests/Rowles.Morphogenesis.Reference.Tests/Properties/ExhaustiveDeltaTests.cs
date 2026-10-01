using Rowles.Morphogenesis.Reference.Energy;
using Rowles.Morphogenesis.Reference.Diagnostics;
using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Tests.Fixtures;

namespace Rowles.Morphogenesis.Reference.Tests.Properties;

public sealed class ExhaustiveDeltaTests
{
    private static readonly GridPoint[] CopyOffsets =
    [
        new(0, -1),
        new(1, 0),
        new(0, 1),
        new(-1, 0)
    ];

    [Fact]
    public void Every_copy_edge_on_every_binary_three_by_three_periodic_state_matches_full_recomputation()
    {
        const int width = 3;
        const int height = 3;
        int compared = 0;
        for (int pattern = 0; pattern < 1 << (width * height); pattern++)
        {
            string[] rows = new string[height];
            for (int row = 0; row < height; row++)
            {
                int y = height - 1 - row;
                char[] cells = new char[width];
                for (int x = 0; x < width; x++)
                {
                    int bit = y * width + x;
                    cells[x] = (pattern & (1 << bit)) == 0 ? '.' : 'A';
                }

                rows[row] = new string(cells);
            }

            Rowles.Morphogenesis.Reference.Model.ReferenceState state = TestStateFactory.FromRows(rows);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    GridPoint target = new(x, y);
                    foreach (GridPoint offset in CopyOffsets)
                    {
                        GridPoint source = PeriodicLattice.Resolve(target, offset, width, height);
                        if (state.CellIdAt(source) == state.CellIdAt(target))
                        {
                            continue;
                        }

                        AssertEquivalent(state, source, target);
                        compared++;
                    }
                }
            }
        }

        Assert.True(compared > 5_000, $"Expected broad binary-state coverage, compared {compared} proposals.");
    }

    [Fact]
    public void Deterministic_random_three_id_states_match_full_recomputation()
    {
        Xoshiro256StarStar random = new(0xD1FF_EA5E_5EED_2026UL);
        char[] symbols = ['.', 'A', 'a', 'B'];
        int compared = 0;
        for (int stateIndex = 0; stateIndex < 200; stateIndex++)
        {
            string[] rows = new string[5];
            for (int row = 0; row < rows.Length; row++)
            {
                char[] values = new char[5];
                for (int x = 0; x < values.Length; x++)
                {
                    values[x] = symbols[random.NextInt(symbols.Length)];
                }

                rows[row] = new string(values);
            }

            Rowles.Morphogenesis.Reference.Model.ReferenceState state = TestStateFactory.FromRows(
                rows,
                new Dictionary<int, int> { [1] = 1, [2] = 1, [3] = 2 });
            GridPoint target = new(random.NextInt(state.Width), random.NextInt(state.Height));
            GridPoint source = PeriodicLattice.Resolve(target, CopyOffsets[random.NextInt(CopyOffsets.Length)], state.Width, state.Height);
            if (state.CellIdAt(source) == state.CellIdAt(target))
            {
                continue;
            }

            AssertEquivalent(state, source, target);
            compared++;
        }

        Assert.True(compared > 100, $"Expected broad random-state coverage, compared {compared} proposals.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Every_centre_copy_on_every_three_valued_three_by_three_state_matches_full_recomputation(int typeB)
    {
        const int width = 3;
        const int height = 3;
        const int stateCount = 19_683;
        int compared = 0;
        for (int pattern = 0; pattern < stateCount; pattern++)
        {
            int encoded = pattern;
            string[] rows = new string[height];
            for (int row = 0; row < height; row++)
            {
                char[] cells = new char[width];
                for (int x = 0; x < width; x++)
                {
                    cells[x] = (encoded % 3) switch
                    {
                        0 => '.',
                        1 => 'A',
                        _ => 'B'
                    };
                    encoded /= 3;
                }

                rows[row] = new string(cells);
            }

            Rowles.Morphogenesis.Reference.Model.ReferenceState state = TestStateFactory.FromRows(
                rows,
                new Dictionary<int, int> { [1] = 1, [3] = typeB });
            GridPoint target = new(1, 1);
            foreach (GridPoint offset in CopyOffsets)
            {
                GridPoint source = PeriodicLattice.Resolve(target, offset, width, height);
                if (state.CellIdAt(source) == state.CellIdAt(target))
                {
                    continue;
                }

                AssertEquivalent(state, source, target);
                compared++;
            }
        }

        Assert.True(compared > 30_000, $"Expected broad three-valued coverage, compared {compared} proposals.");
    }

    private static void AssertEquivalent(
        Rowles.Morphogenesis.Reference.Model.ReferenceState state,
        GridPoint source,
        GridPoint target)
    {
        MoveDelta local = LocalMoveDelta.Evaluate(state, source, target);
        BruteForceMove full = BruteForceMoveReference.Evaluate(state, source, target);

        AssertWhenMismatch(full.Terms.Contact, local.Terms.Contact, "Contact", state, source, target);
        AssertWhenMismatch(full.Terms.Area, local.Terms.Area, "Area", state, source, target);
        AssertWhenMismatch(full.Terms.Perimeter, local.Terms.Perimeter, "Perimeter", state, source, target);
        AssertWhenMismatch(full.TotalDelta, local.Total, "Total", state, source, target);
    }

    private static void AssertWhenMismatch(
        double expected,
        double actual,
        string term,
        Rowles.Morphogenesis.Reference.Model.ReferenceState state,
        GridPoint source,
        GridPoint target)
    {
        if (!EnergyComparison.NearlyEqual(expected, actual))
        {
            Assert.Fail($"{term} local/global mismatch: expected {expected:R}, actual {actual:R}.\n" +
                ReferenceDiagnostics.FormatMove(state, source, target, 1));
        }
    }
}
