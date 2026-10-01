using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Tests.Fixtures;

namespace Rowles.Morphogenesis.Reference.Tests.Lattice;

public sealed class PeriodicNeighbourTests
{
    [Fact]
    public void Canonical_neighbourhood_roles_are_explicit_and_distinct()
    {
        ModelConventions conventions = ModelConventions.Canonical;

        Assert.Same(CopyNeighbourhood.VonNeumann, conventions.CopyNeighbourhood);
        Assert.Same(ContactCouplingNeighbourhood.Moore, conventions.ContactCouplingNeighbourhood);
        Assert.Same(PerimeterNeighbourhood.Moore, conventions.PerimeterNeighbourhood);
        Assert.Same(ConnectivityAdjacency.VonNeumann, conventions.ConnectivityAdjacency);
        Assert.NotSame(conventions.CopyNeighbourhood, (object)conventions.ConnectivityAdjacency);
        Assert.NotSame(conventions.ContactCouplingNeighbourhood, (object)conventions.PerimeterNeighbourhood);
        Assert.Equal(4, conventions.CopyNeighbourhood.Count);
    }

    [Theory]
    [InlineData(-1, 5, 4)]
    [InlineData(5, 5, 0)]
    [InlineData(-6, 5, 4)]
    [InlineData(11, 5, 1)]
    public void Periodic_wrap_resolves_coordinates(int coordinate, int length, int expected)
    {
        Assert.Equal(expected, PeriodicLattice.Wrap(coordinate, length));
    }

    [Fact]
    public void Moore_corner_neighbour_wraps_both_axes()
    {
        GridPoint resolved = PeriodicLattice.Resolve(new GridPoint(0, 0), new GridPoint(-1, -1), 5, 4);

        Assert.Equal(new GridPoint(4, 3), resolved);
    }

    [Theory]
    [InlineData(2, 5)]
    [InlineData(5, 2)]
    public void Construction_rejects_periodic_dimensions_that_alias_stencil_neighbours(int width, int height)
    {
        int[,] ids = new int[width, height];

        Assert.Throws<ArgumentException>(() => TestStateFactory.FromRows(ToRows(ids)));
    }

    [Fact]
    public void Copy_source_selection_wraps_over_each_periodic_edge()
    {
        ReferenceState state = TestStateFactory.FromRows(
        [
            ".....",
            ".....",
            "A...A",
            ".....",
            "....."
        ]);
        ScriptedRandomSource leftWrap = new([2 * state.Width + 0, 3]);
        ScriptedRandomSource rightWrap = new([2 * state.Width + 4, 1]);
        ScriptedRandomSource topWrap = new([0 * state.Width + 2, 0]);
        ScriptedRandomSource bottomWrap = new([4 * state.Width + 2, 2]);

        AttemptResult left = new ReferenceSimulation(state, leftWrap, 1).Attempt();
        AttemptResult right = new ReferenceSimulation(state, rightWrap, 1).Attempt();
        AttemptResult top = new ReferenceSimulation(state, topWrap, 1).Attempt();
        AttemptResult bottom = new ReferenceSimulation(state, bottomWrap, 1).Attempt();

        Assert.Equal(new GridPoint(4, 2), left.Source);
        Assert.Equal(new GridPoint(0, 2), right.Source);
        Assert.Equal(new GridPoint(2, 4), top.Source);
        Assert.Equal(new GridPoint(2, 0), bottom.Source);
    }

    private static string[] ToRows(int[,] ids)
    {
        string[] rows = new string[ids.GetLength(1)];
        for (int row = 0; row < rows.Length; row++)
        {
            int y = rows.Length - 1 - row;
            char[] values = new char[ids.GetLength(0)];
            for (int x = 0; x < values.Length; x++)
            {
                values[x] = ids[x, y] == 0 ? '.' : 'A';
            }

            rows[row] = new string(values);
        }

        return rows;
    }
}
