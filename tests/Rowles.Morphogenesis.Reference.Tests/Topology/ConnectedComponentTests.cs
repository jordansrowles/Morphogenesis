using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Tests.Fixtures;
using Rowles.Morphogenesis.Reference.Topology;

namespace Rowles.Morphogenesis.Reference.Tests.Topology;

public sealed class ConnectedComponentTests
{
    [Fact]
    public void Diagonal_only_contact_does_not_connect_under_von_neumann_adjacency()
    {
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", "..A..", "...A.", "....."]);

        Assert.Equal(2, ConnectivityReference.CountComponents(state, 1));
        Assert.False(ConnectivityReference.IsConnected(state, 1));
    }

    [Fact]
    public void G07_removing_a_bridge_site_detects_fragmentation()
    {
        ReferenceState state = TestStateFactory.FromRows(
            [".......", ".......", "...A...", "...A...", "...A...", ".......", "......."]);
        GridPoint source = new(2, 3);
        GridPoint target = new(3, 3);

        Assert.Equal(1, ConnectivityReference.CountComponents(state, 1));
        ProposedConnectivityCheck check = ConnectivityReference.EvaluateCopy(state, source, target);

        Assert.Equal(2, check.ComponentsAfter);
        Assert.False(check.WouldRemainConnected);
        Assert.True(ConnectivityReference.IsConnected(state, 1));
        Assert.Equal(1, state.CellIdAt(target));
    }

    [Fact]
    public void Removing_a_non_bridge_boundary_site_preserves_one_component()
    {
        ReferenceState state = TestStateFactory.FromRows(
            [".......", ".......", "..AA...", "..AA...", ".......", ".......", "......."]);
        GridPoint source = new(1, 3);
        GridPoint target = new(2, 3);

        ProposedConnectivityCheck check = ConnectivityReference.EvaluateCopy(state, source, target);

        Assert.Equal(1, check.ComponentsAfter);
        Assert.True(check.WouldRemainConnected);
    }

    [Fact]
    public void Removing_a_single_site_reports_zero_components_and_not_connected()
    {
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", "..A..", ".....", "....."]);
        GridPoint source = new(1, 2);
        GridPoint target = new(2, 2);

        Assert.Equal(1, ConnectivityReference.CountComponents(state, 1));
        ProposedConnectivityCheck check = ConnectivityReference.EvaluateCopy(state, source, target);

        Assert.Equal(0, check.ComponentsAfter);
        Assert.False(check.WouldRemainConnected);
    }

    [Fact]
    public void G08_ring_is_connected_while_its_medium_complement_has_two_components()
    {
        ReferenceState state = TestStateFactory.FromRows(
            [".......", ".......", "..AAA..", "..A.A..", "..AAA..", ".......", "......."]);

        Assert.True(ConnectivityReference.IsConnected(state, 1));
        Assert.Equal(1, ConnectivityReference.CountComponents(state, 1));
        Assert.Equal(2, ConnectivityReference.CountComponents(state, 0));
    }

    [Fact]
    public void Copy_into_medium_has_no_losing_biological_cell()
    {
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", "..A..", ".....", "....."]);

        ProposedConnectivityCheck check = ConnectivityReference.EvaluateCopy(state, new GridPoint(2, 2), new GridPoint(2, 1));

        Assert.Equal(0, check.LosingCellId);
        Assert.Null(check.ComponentsAfter);
        Assert.True(check.WouldRemainConnected);
    }
}
