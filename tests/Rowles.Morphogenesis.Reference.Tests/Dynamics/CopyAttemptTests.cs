using Rowles.Morphogenesis.Reference.Dynamics;
using Rowles.Morphogenesis.Reference.Energy;
using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Model;
using Rowles.Morphogenesis.Reference.Tests.Fixtures;

namespace Rowles.Morphogenesis.Reference.Tests.Dynamics;

public sealed class CopyAttemptTests
{
    [Fact]
    public void Same_id_proposal_is_a_no_op_without_an_acceptance_draw()
    {
        ReferenceState state = TestStateFactory.FromRows(["AAAAA", "AAAAA", "AAAAA", "AAAAA", "AAAAA"]);
        ScriptedRandomSource random = new([12, 0]);
        int[,] before = state.GetCellIdsCopy();

        AttemptResult result = new ReferenceSimulation(state, random, 1).Attempt();

        Assert.Equal(AttemptStatus.NoOp, result.Status);
        Assert.Equal(new HamiltonianBreakdown(0, 0, 0), result.DeltaH);
        Assert.Null(result.AcceptanceRandomValue);
        Assert.Equal(0, random.AcceptanceDrawCount);
        AssertLatticeEqual(before, state.GetCellIdsCopy());
        random.AssertExhausted();
    }

    [Fact]
    public void Non_positive_delta_is_accepted_without_an_acceptance_draw()
    {
        CellDefinition cell = new(1, 1, TargetArea: 2, AreaStiffness: 1, TargetPerimeter: 0, PerimeterStiffness: 0);
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", "..A..", ".....", "....."],
            definitions: [cell],
            contactEnergies: new double[,] { { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 } });
        ScriptedRandomSource random = new([7, 2]);

        AttemptResult result = new ReferenceSimulation(state, random, 1).Attempt();

        Assert.Equal(AttemptStatus.Accepted, result.Status);
        Assert.Equal(-1, result.DeltaH.Area);
        Assert.Null(result.AcceptanceRandomValue);
        Assert.Equal(0, random.AcceptanceDrawCount);
        Assert.Equal(1, state.CellIdAt(result.Target));
        Assert.Equal(1, state.CellIdAt(result.Source));
        random.AssertExhausted();
    }

    [Fact]
    public void Positive_delta_rejection_does_not_mutate_any_site()
    {
        CellDefinition cell = new(1, 1, TargetArea: 1, AreaStiffness: 2, TargetPerimeter: 8, PerimeterStiffness: 0);
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", "..A..", ".....", "....."],
            definitions: [cell],
            contactEnergies: new double[,] { { 0, 1, 0 }, { 1, 0, 0 }, { 0, 0, 0 } });
        ScriptedRandomSource random = new([7, 2], [0.5]);
        int[,] before = state.GetCellIdsCopy();

        AttemptResult result = new ReferenceSimulation(state, random, 0.5).Attempt();

        Assert.Equal(AttemptStatus.Rejected, result.Status);
        Assert.True(result.DeltaH.Total > 0);
        Assert.Equal(0.5, result.AcceptanceRandomValue);
        Assert.Equal(1, random.AcceptanceDrawCount);
        AssertLatticeEqual(before, state.GetCellIdsCopy());
        random.AssertExhausted();
    }

    [Fact]
    public void Positive_delta_at_zero_amplitude_still_consumes_one_draw()
    {
        CellDefinition cell = new(1, 1, TargetArea: 1, AreaStiffness: 2, TargetPerimeter: 8, PerimeterStiffness: 0);
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", "..A..", ".....", "....."],
            definitions: [cell],
            contactEnergies: new double[,] { { 0, 1, 0 }, { 1, 0, 0 }, { 0, 0, 0 } });
        ScriptedRandomSource random = new([7, 2], [0.25]);

        AttemptResult result = new ReferenceSimulation(state, random, 0).Attempt();

        Assert.Equal(AttemptStatus.Rejected, result.Status);
        Assert.Equal(0, result.AcceptanceProbability);
        Assert.Equal(0.25, result.AcceptanceRandomValue);
        Assert.Equal(1, random.AcceptanceDrawCount);
        random.AssertExhausted();
    }

    [Fact]
    public void Accepted_positive_delta_changes_only_the_target_site()
    {
        CellDefinition cell = new(1, 1, TargetArea: 1, AreaStiffness: 2, TargetPerimeter: 8, PerimeterStiffness: 0);
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", "..A..", ".....", "....."],
            definitions: [cell],
            contactEnergies: new double[,] { { 0, 1, 0 }, { 1, 0, 0 }, { 0, 0, 0 } });
        ScriptedRandomSource random = new([7, 2], [0]);
        int[,] before = state.GetCellIdsCopy();

        AttemptResult result = new ReferenceSimulation(state, random, 100).Attempt();
        int changedSites = 0;
        for (int x = 0; x < state.Width; x++)
        {
            for (int y = 0; y < state.Height; y++)
            {
                if (before[x, y] != state.CellIdAt(x, y))
                {
                    changedSites++;
                    Assert.Equal(result.Target, new GridPoint(x, y));
                }
            }
        }

        Assert.Equal(AttemptStatus.Accepted, result.Status);
        Assert.Equal(1, changedSites);
        Assert.Equal(before[result.Source.X, result.Source.Y], state.CellIdAt(result.Source));
        Assert.Equal(1, state.CellTypeIdForCell(1));
        Assert.Equal(state.SiteCount, state.Width * state.Height);
    }

    [Fact]
    public void Attempt_draw_order_is_target_then_copy_neighbour_then_acceptance()
    {
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", "..A..", ".....", "....."],
            definitions: [new CellDefinition(1, 1, 1, 2, 8, 0)],
            contactEnergies: new double[,] { { 0, 1, 0 }, { 1, 0, 0 }, { 0, 0, 0 } });
        ScriptedRandomSource random = new([7, 2], [0.9]);

        AttemptResult result = new ReferenceSimulation(state, random, 0.1).Attempt();

        Assert.Equal(new GridPoint(2, 1), result.Target);
        Assert.Equal(new GridPoint(2, 2), result.Source);
        Assert.Equal([25, 4], random.IntegerUpperBounds);
        Assert.Equal(2, random.IntegerDrawCount);
        Assert.Equal(1, random.AcceptanceDrawCount);
        Assert.Equal(AttemptStatus.Rejected, result.Status);
    }

    [Fact]
    public void One_mcs_runs_exactly_width_times_height_attempts_including_no_ops()
    {
        ReferenceState state = TestStateFactory.FromRows([".....", ".....", ".....", ".....", "....."]);
        ScriptedRandomSource random = new(Enumerable.Repeat(0, state.SiteCount * 2));
        ReferenceSimulation simulation = new(state, random, 0);

        IReadOnlyList<AttemptResult> attempts = simulation.RunMcs();

        Assert.Equal(state.Width * state.Height, attempts.Count);
        Assert.All(attempts, attempt => Assert.Equal(AttemptStatus.NoOp, attempt.Status));
        Assert.Equal(state.SiteCount * 2, random.IntegerDrawCount);
        Assert.Equal(0, random.AcceptanceDrawCount);
        Assert.Equal(1, simulation.CompletedMcs);
        random.AssertExhausted();
    }

    private static void AssertLatticeEqual(int[,] expected, int[,] actual)
    {
        Assert.Equal(expected.GetLength(0), actual.GetLength(0));
        Assert.Equal(expected.GetLength(1), actual.GetLength(1));
        for (int x = 0; x < expected.GetLength(0); x++)
        {
            for (int y = 0; y < expected.GetLength(1); y++)
            {
                Assert.Equal(expected[x, y], actual[x, y]);
            }
        }
    }
}
