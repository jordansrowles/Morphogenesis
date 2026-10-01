using Rowles.Morphogenesis.Reference.Dynamics;
using Rowles.Morphogenesis.Reference.Model;
using Rowles.Morphogenesis.Reference.Tests.Fixtures;

namespace Rowles.Morphogenesis.Reference.Tests.Dynamics;

public sealed class AcceptanceTests
{
    [Theory]
    [InlineData(-1, 1, 1)]
    [InlineData(0, 1, 1)]
    [InlineData(0.0001, 1, 0.9999000049998333)]
    [InlineData(1000, 1, 0)]
    [InlineData(1, 0, 0)]
    public void Modified_metropolis_probability_obeys_frozen_edges(double deltaH, double fluctuation, double expected)
    {
        Assert.Equal(expected, ReferenceSimulation.AcceptanceProbability(deltaH, fluctuation));
    }

    [Fact]
    public void T_zero_accepts_non_positive_delta_and_rejects_positive_delta()
    {
        Assert.Equal(1, ReferenceSimulation.AcceptanceProbability(-0.25, 0));
        Assert.Equal(1, ReferenceSimulation.AcceptanceProbability(0, 0));
        Assert.Equal(0, ReferenceSimulation.AcceptanceProbability(0.25, 0));
    }

    [Fact]
    public void Acceptance_draw_is_compared_strictly_with_probability()
    {
        Assert.True(ReferenceSimulation.ShouldAccept(0.5, 0.4));
        Assert.False(ReferenceSimulation.ShouldAccept(0.5, 0.5));
        Assert.False(ReferenceSimulation.ShouldAccept(0.5, 0.6));
    }

    [Theory]
    [InlineData(0.4, AttemptStatus.Accepted)]
    [InlineData(0.6, AttemptStatus.Rejected)]
    public void G09_threshold_fixture_uses_scripted_values_clear_of_probability_edge(
        double acceptanceValue,
        AttemptStatus expectedStatus)
    {
        CellDefinition cell = new(1, 1, TargetArea: 0, AreaStiffness: 1, TargetPerimeter: 0, PerimeterStiffness: 0);
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", ".A...", ".....", "....."],
            definitions: [cell],
            contactEnergies: new double[,] { { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 } });
        ScriptedRandomSource random = new([12, 3], [acceptanceValue]);

        AttemptResult result = new ReferenceSimulation(state, random, 3 / Math.Log(2)).Attempt();

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(3, result.DeltaH.Area);
        Assert.True(Math.Abs(result.AcceptanceProbability!.Value - 0.5) < 1e-15);
        Assert.Equal(acceptanceValue, result.AcceptanceRandomValue);
        Assert.Equal([25, 4], random.IntegerUpperBounds);
        Assert.Equal(1, random.AcceptanceDrawCount);
        Assert.Equal(expectedStatus == AttemptStatus.Accepted ? 1 : 0, state.CellIdAt(2, 2));
        random.AssertExhausted();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Invalid_fluctuation_amplitudes_are_rejected(double fluctuation)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReferenceSimulation.AcceptanceProbability(1, fluctuation));
    }

    [Fact]
    public void NaN_energy_change_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReferenceSimulation.AcceptanceProbability(double.NaN, 1));
    }
}
