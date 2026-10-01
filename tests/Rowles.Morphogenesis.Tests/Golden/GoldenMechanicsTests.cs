using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Tests.Fixtures;

namespace Rowles.Morphogenesis.Tests.Golden;

public sealed class GoldenMechanicsTests
{
    [Fact]
    public void G00_uniform_medium_has_zero_energy_and_a_same_id_no_op()
    {
        MorphogenesisState state = new(
            5,
            5,
            new int[25],
            [],
            new ContactEnergyMatrix(new double[,] { { 0 } }));
        ScriptedRandomSource random = new([12, 0]);
        SerialSimulation simulation = new(state, random, 1);
        HamiltonianBreakdown energy = state.RecomputeHamiltonian();

        AttemptResult result = simulation.Attempt();

        Assert.Equal(new HamiltonianBreakdown(0, 0, 0), energy);
        Assert.Equal(AttemptStatus.NoOp, result.Status);
        Assert.Equal(default, result.DeltaH);
        Assert.Null(result.AcceptanceRandomValue);
        Assert.Equal(2, random.IntegerDrawCount);
        random.AssertExhausted();
    }

    [Fact]
    public void G01_and_G02_contact_ledgers_match_the_frozen_reference_values()
    {
        double[,] defaultContacts =
        {
            { 0, 3, 4 },
            { 3, 2, 5 },
            { 4, 5, 1 }
        };
        CellDefinition g01Cell = new(1, 1, 0, 0, 0, 0);
        MorphogenesisState g01 = ProductionTestStateFactory.FromRows(
            [".....", ".....", "..A..", ".....", "....."],
            [g01Cell],
            defaultContacts);
        HamiltonianBreakdown g01Before = g01.RecomputeHamiltonian();
        MoveEvaluation g01Move = EnergyDeltaCalculator.Evaluate(g01, 2 * 5 + 3, 1);
        int[] afterIds = g01.GetCellIdsCopy();
        afterIds[2 * 5 + 3] = 1;
        MorphogenesisState g01After = new(5, 5, afterIds, [g01Cell], new ContactEnergyMatrix(defaultContacts));

        Assert.Equal(24, g01Before.Contact);
        Assert.Equal(18, g01Move.Terms.Contact);
        Assert.Equal(42, g01After.RecomputeHamiltonian().Contact);

        double[,] mediumContacts = { { 0, 2 }, { 2, 0 } };
        CellDefinition g02Cell = new(1, 1, 1, 0, 8, 0);
        MorphogenesisState g02 = ProductionTestStateFactory.FromRows(
            [".....", ".....", "..A..", ".....", "....."],
            [g02Cell],
            mediumContacts);
        MoveEvaluation g02Move = EnergyDeltaCalculator.Evaluate(g02, 2 * 5 + 3, 1);

        Assert.Equal(16, g02.RecomputeHamiltonian().Contact);
        Assert.Equal(12, g02Move.Terms.Contact);
    }

    [Fact]
    public void G04_same_type_different_ids_use_type_contact_but_id_based_interfaces()
    {
        string[] rows = [".....", ".....", ".Aa..", ".....", "....."];
        CellDefinition[] cells = [new(1, 1, 1, 0.5, 8, 0.1), new(2, 1, 1, 0.5, 8, 0.1)];
        double[,] contacts = { { 0, 2 }, { 2, 2 } };
        MorphogenesisState state = ProductionTestStateFactory.FromRows(rows, cells, contacts);
        Rowles.Morphogenesis.Reference.Model.ReferenceState reference =
            ProductionTestStateFactory.ToReference(rows, cells, contacts);
        MoveEvaluation actual = EnergyDeltaCalculator.Evaluate(state, 2 * 5 + 2, 1);
        Rowles.Morphogenesis.Reference.Energy.MoveDelta expected =
            Rowles.Morphogenesis.Reference.Energy.LocalMoveDelta.Evaluate(
                reference,
                new Rowles.Morphogenesis.Reference.Lattice.GridPoint(1, 2),
                new Rowles.Morphogenesis.Reference.Lattice.GridPoint(2, 2));

        Assert.Equal(-2, actual.Terms.Contact);
        AssertTermsEqual(expected.Terms, actual.Terms);
    }

    [Fact]
    public void G05_periodic_seam_copy_matches_complete_reference_recomputation()
    {
        string[] rows = ["...", "B.A", "..."];
        CellDefinition[] cells = [new(1, 1, 1, 0.7, 8, 0.1), new(3, 2, 1, 0.8, 8, 0.1)];
        double[,] contacts =
        {
            { 0, 3, 3 },
            { 3, 2, 4 },
            { 3, 4, 1 }
        };
        MorphogenesisState state = ProductionTestStateFactory.FromRows(rows, cells, contacts);
        Rowles.Morphogenesis.Reference.Model.ReferenceState reference =
            ProductionTestStateFactory.ToReference(rows, cells, contacts);
        MoveEvaluation actual = EnergyDeltaCalculator.Evaluate(state, 1 * 3 + 2, 3);
        Rowles.Morphogenesis.Reference.Energy.BruteForceMove expected =
            Rowles.Morphogenesis.Reference.Energy.BruteForceMoveReference.Evaluate(
                reference,
                new Rowles.Morphogenesis.Reference.Lattice.GridPoint(0, 1),
                new Rowles.Morphogenesis.Reference.Lattice.GridPoint(2, 1));

        Assert.Equal(46, state.RecomputeHamiltonian().Contact);
        Assert.Equal(-4, actual.Terms.Contact);
        AssertTermsEqual(expected.Terms, actual.Terms);
    }

    [Theory]
    [InlineData("horizontal", 14)]
    [InlineData("vertical", 14)]
    [InlineData("square", 20)]
    [InlineData("L", 18)]
    [InlineData("thin-line", 20)]
    public void G06_hand_calculated_perimeter_shapes_match(string shape, int expectedPerimeter)
    {
        string[] rows = shape switch
        {
            "horizontal" => [".......", ".......", "..AA...", ".......", "......."],
            "vertical" => [".......", "...A...", "...A...", ".......", "......."],
            "square" => [".......", ".......", "..AA...", "..AA...", "......."],
            "L" => [".......", ".......", "..AA...", "..A....", "......."],
            "thin-line" => [".......", ".......", ".AAA...", ".......", "......."],
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        int area = rows.Sum(row => row.Count(symbol => symbol == 'A'));
        MorphogenesisState state = ProductionTestStateFactory.FromRows(
            rows,
            [new CellDefinition(1, 1, area, 0, expectedPerimeter, 0)],
            new double[,] { { 0, 2 }, { 2, 0 } });

        Assert.Equal(expectedPerimeter, state.GetCellState(1).Perimeter);
        state.ValidateInvariants();
    }

    [Fact]
    public void G06_cell_spanning_a_periodic_seam_has_domino_perimeter()
    {
        MorphogenesisState state = ProductionTestStateFactory.FromRows(
            [".....", ".....", "A...A", ".....", "....."],
            [new CellDefinition(1, 1, 2, 0, 14, 0)],
            new double[,] { { 0, 2 }, { 2, 0 } });

        Assert.Equal(14, state.GetCellState(1).Perimeter);
    }

    [Theory]
    [InlineData(0.4, AttemptStatus.Accepted)]
    [InlineData(0.6, AttemptStatus.Rejected)]
    public void G09_scripted_probability_threshold_matches_reference(double acceptanceValue, AttemptStatus expected)
    {
        double fluctuation = 3 / Math.Log(2);
        int[] ids = new int[25];
        ids[2 * 5 + 1] = 1;
        MorphogenesisState state = new(
            5,
            5,
            ids,
            [new CellDefinition(1, 1, 0, 1, 0, 0)],
            new ContactEnergyMatrix(new double[,] { { 0, 0 }, { 0, 0 } }));
        ScriptedRandomSource random = new([12, 3], [acceptanceValue]);
        AttemptResult result = new SerialSimulation(state, random, fluctuation).Attempt();

        Assert.Equal(expected, result.Status);
        Assert.Equal(3, result.DeltaH.Area);
        Assert.True(Math.Abs(result.AcceptanceProbability!.Value - 0.5) < 1e-15);
        Assert.Equal(acceptanceValue, result.AcceptanceRandomValue);
        Assert.Equal(expected == AttemptStatus.Accepted ? 1 : 0, state.CellIdAt(2, 2));
        Assert.Equal(1, random.AcceptanceDrawCount);
        random.AssertExhausted();
    }

    private static void AssertTermsEqual(
        Rowles.Morphogenesis.Reference.Energy.HamiltonianBreakdown expected,
        HamiltonianBreakdown actual)
    {
        AssertClose(expected.Contact, actual.Contact);
        AssertClose(expected.Area, actual.Area);
        AssertClose(expected.Perimeter, actual.Perimeter);
        AssertClose(expected.Total, actual.Total);
    }

    private static void AssertClose(double expected, double actual) =>
        Assert.True(Math.Abs(expected - actual) <= 1e-10 + 1e-12 * Math.Max(Math.Abs(expected), Math.Abs(actual)),
            $"Expected {expected:R}, actual {actual:R}.");

    private sealed class ScriptedRandomSource(IEnumerable<int> integerValues, IEnumerable<double>? acceptanceValues = null)
        : Rowles.Morphogenesis.Random.IRandomSource
    {
        private readonly Queue<int> _integers = new(integerValues);
        private readonly Queue<double> _acceptance = new(acceptanceValues ?? []);

        internal int IntegerDrawCount { get; private set; }

        internal int AcceptanceDrawCount { get; private set; }

        public int NextInt(int exclusiveUpperBound)
        {
            IntegerDrawCount++;
            if (!_integers.TryDequeue(out int value) || (uint)value >= (uint)exclusiveUpperBound)
            {
                throw new InvalidOperationException("Unexpected scripted integer draw.");
            }

            return value;
        }

        public double NextDouble()
        {
            AcceptanceDrawCount++;
            return _acceptance.TryDequeue(out double value)
                ? value
                : throw new InvalidOperationException("Unexpected scripted acceptance draw.");
        }

        internal void AssertExhausted()
        {
            Assert.Empty(_integers);
            Assert.Empty(_acceptance);
        }
    }
}
