using Rowles.Morphogenesis.Reference.Dynamics;
using Rowles.Morphogenesis.Reference.Energy;
using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Model;
using Rowles.Morphogenesis.Reference.Tests.Fixtures;

namespace Rowles.Morphogenesis.Reference.Tests.Golden;

public sealed class GoldenFixtureTests
{
    [Fact]
    public void G00_uniform_state_has_no_contact_energy_and_same_id_copy_is_a_no_op()
    {
        ReferenceState state = TestStateFactory.FromRows([".....", ".....", ".....", ".....", "....."]);
        BruteForceMove move = BruteForceMoveReference.Evaluate(state, new GridPoint(2, 1), new GridPoint(2, 2));

        Assert.Equal(new HamiltonianBreakdown(0, 0, 0), move.Before);
        Assert.Equal(new HamiltonianBreakdown(0, 0, 0), move.Terms);
        Assert.Equal(move.Before, move.After);
    }

    [Fact]
    public void G01_single_interface_has_hand_calculated_contact_and_local_change()
    {
        CellDefinition cell = new(1, 1, 0, 0, 0, 0);
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", "..A..", ".....", "....."],
            definitions: [cell]);
        GridPoint source = new(2, 2);
        GridPoint target = new(3, 2);
        BruteForceMove full = BruteForceMoveReference.Evaluate(state, source, target);
        MoveDelta local = LocalMoveDelta.Evaluate(state, source, target);

        Assert.Equal(24, full.Before.Contact);
        Assert.Equal(42, full.After.Contact);
        Assert.Equal(18, full.TotalDelta);
        Assert.Equal(full.Terms, local.Terms);
    }

    [Fact]
    public void G02_medium_interface_has_cell_medium_energy_but_no_medium_constraint()
    {
        CellDefinition cell = new(1, 1, 1, 0, 8, 0);
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", "..A..", ".....", "....."],
            definitions: [cell],
            contactEnergies: new double[,] { { 0, 2, 2 }, { 2, 0, 2 }, { 2, 2, 0 } });
        BruteForceMove growth = BruteForceMoveReference.Evaluate(state, new GridPoint(2, 2), new GridPoint(3, 2));
        MoveDelta growthLocal = LocalMoveDelta.Evaluate(state, new GridPoint(2, 2), new GridPoint(3, 2));
        BruteForceMove retraction = BruteForceMoveReference.Evaluate(state, new GridPoint(1, 2), new GridPoint(2, 2));

        Assert.Equal(16, growth.Before.Contact);
        Assert.Equal(28, growth.After.Contact);
        Assert.Equal(12, growth.TotalDelta);
        Assert.Equal(growth.Terms, growthLocal.Terms);
        Assert.Equal(-16, retraction.TotalDelta);
        Assert.Equal(0, growth.Before.Area);
        Assert.Equal(0, growth.Before.Perimeter);
    }

    [Fact]
    public void G03_biological_copy_combines_source_gain_target_loss_and_interfaces()
    {
        CellDefinition sourceCell = new(1, 1, 2, 1.5, 12, 0.2);
        CellDefinition targetCell = new(3, 2, 1, 0.75, 8, 0.1);
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", ".AB..", ".....", "....."],
            cellTypes: new Dictionary<int, int> { [1] = 1, [3] = 2 },
            definitions: [sourceCell, targetCell]);
        GridPoint source = new(1, 2);
        GridPoint target = new(2, 2);
        BruteForceMove full = BruteForceMoveReference.Evaluate(state, source, target);
        MoveDelta local = LocalMoveDelta.Evaluate(state, source, target);

        Assert.NotEqual(0, full.Terms.Area);
        Assert.NotEqual(0, full.Terms.Contact);
        Assert.NotEqual(0, full.Terms.Perimeter);
        AssertTermsEqual(full.Terms, local.Terms);
    }

    [Fact]
    public void G04_same_type_different_ids_use_type_J_but_id_boundary_predicate()
    {
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", ".Aa..", ".....", "....."],
            cellTypes: new Dictionary<int, int> { [1] = 1, [2] = 1 });
        GridPoint source = new(1, 2);
        GridPoint target = new(2, 2);
        MoveDelta local = LocalMoveDelta.Evaluate(state, source, target);
        BruteForceMove full = BruteForceMoveReference.Evaluate(state, source, target);

        Assert.Equal(2, state.ContactEnergies[1, 1]);
        Assert.Equal(-2, local.Terms.Contact);
        AssertTermsEqual(full.Terms, local.Terms);
    }

    [Fact]
    public void G05_periodic_seam_proposal_matches_global_edge_count()
    {
        ReferenceState state = TestStateFactory.FromRows(
            ["...", "B.A", "..."],
            cellTypes: new Dictionary<int, int> { [1] = 1, [3] = 2 },
            contactEnergies: new double[,] { { 0, 3, 3 }, { 3, 2, 4 }, { 3, 4, 1 } });
        GridPoint source = new(0, 1);
        GridPoint target = new(2, 1);

        Assert.Equal(46, ReferenceEnergy.ComputeContactEnergy(state));
        Assert.Equal(-4, LocalMoveDelta.Evaluate(state, source, target).Terms.Contact);
        AssertTermsEqual(
            BruteForceMoveReference.Evaluate(state, source, target).Terms,
            LocalMoveDelta.Evaluate(state, source, target).Terms);
    }

    private static void AssertTermsEqual(HamiltonianBreakdown expected, HamiltonianBreakdown actual)
    {
        Assert.True(EnergyComparison.NearlyEqual(expected.Contact, actual.Contact));
        Assert.True(EnergyComparison.NearlyEqual(expected.Area, actual.Area));
        Assert.True(EnergyComparison.NearlyEqual(expected.Perimeter, actual.Perimeter));
        Assert.True(EnergyComparison.NearlyEqual(expected.Total, actual.Total));
    }
}
