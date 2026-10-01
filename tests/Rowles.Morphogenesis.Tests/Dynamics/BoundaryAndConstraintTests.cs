using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Tests.Fixtures;

namespace Rowles.Morphogenesis.Tests.Dynamics;

public sealed class BoundaryAndConstraintTests
{
    private static readonly double[,] Contacts = { { 0, 2 }, { 2, 0 } };

    [Fact]
    public void W00_corner_cell_counts_five_wall_contacts_and_perimeter_positions()
    {
        MorphogenesisState state = ProductionTestStateFactory.FromRows(
            ["...", "...", "A.."],
            [new CellDefinition(1, 1, 1, 1, 8, 0.25)],
            Contacts,
            SimulationConfiguration.WallCanonical);

        Assert.Equal(16, state.RecomputeHamiltonian().Contact);
        Assert.Equal(8, state.GetCellState(1).Perimeter);
        Assert.Equal(1, state.GetCellState(1).Area);
    }

    [Fact]
    public void W01_copy_along_wall_matches_hand_calculated_contact_area_and_perimeter_delta()
    {
        MorphogenesisState state = ProductionTestStateFactory.FromRows(
            ["...", "...", "A.."],
            [new CellDefinition(1, 1, 1, 1, 8, 0.25)],
            Contacts,
            SimulationConfiguration.WallCanonical);
        ScriptedRandomSource random = new([1, 3], [0]);
        SerialSimulation simulation = new(state, random, 1000);
        HamiltonianBreakdown before = state.RecomputeHamiltonian();

        AttemptResult result = simulation.Attempt();
        HamiltonianBreakdown after = state.RecomputeHamiltonian();

        Assert.Equal(AttemptStatus.Accepted, result.Status);
        Assert.Equal(new HamiltonianBreakdown(12, 1, 9), result.DeltaH);
        Assert.Equal(16, before.Contact);
        Assert.Equal(28, after.Contact);
        Assert.Equal(new HamiltonianBreakdown(12, 1, 9), new HamiltonianBreakdown(
            after.Contact - before.Contact,
            after.Area - before.Area,
            after.Perimeter - before.Perimeter));
        Assert.Equal(2, state.GetCellState(1).Area);
        Assert.Equal(14, state.GetCellState(1).Perimeter);
        Assert.Equal([9, 4], random.IntegerUpperBounds);
        Assert.Equal(1, random.AcceptanceDrawCount);
        state.ValidateInvariants();
        random.AssertExhausted();
    }

    [Fact]
    public void W02_wall_source_consumes_target_and_direction_draws_only()
    {
        MorphogenesisState state = ProductionTestStateFactory.FromRows(
            ["...", "...", "A.."],
            [new CellDefinition(1, 1, 1, 1, 8, 0.25)],
            Contacts,
            SimulationConfiguration.WallCanonical);
        ScriptedRandomSource random = new([0, 0]);
        SerialSimulation simulation = new(state, random, 1);
        int[] before = state.GetCellIdsCopy();

        AttemptResult result = simulation.Attempt();

        Assert.Equal(AttemptStatus.Rejected, result.Status);
        Assert.Equal(RejectionReason.FixedWall, result.RejectionReason);
        Assert.Equal(-1, result.SourceIndex);
        Assert.Equal(2, random.IntegerDrawCount);
        Assert.Empty(random.AcceptanceValuesRemaining);
        Assert.Equal(before, state.GetCellIdsCopy());
        Assert.Equal(1, state.GetCellState(1).Area);
        Assert.Equal(8, state.GetCellState(1).Perimeter);
        random.AssertExhausted();
    }

    [Fact]
    public void W03_all_medium_wall_state_counts_nonzero_J00_for_every_wall_position()
    {
        double[,] contacts =
        {
            { 1, 2 },
            { 2, 0 }
        };
        MorphogenesisState state = new(
            3,
            3,
            new int[9],
            Array.Empty<CellDefinition>(),
            new ContactEnergyMatrix(contacts),
            SimulationConfiguration.WallCanonical);

        HamiltonianBreakdown energy = state.RecomputeHamiltonian();

        Assert.Equal(32, energy.Contact);
        Assert.Equal(0, energy.Area);
        Assert.Equal(0, energy.Perimeter);
        Assert.Equal(32, energy.Total);
    }

    [Fact]
    public void W04_wall_delta_with_nonzero_J00_matches_full_recomputation()
    {
        double[,] contacts =
        {
            { 1, 2 },
            { 2, 0 }
        };
        CellDefinition[] cells = [new(1, 1, 1, 1, 8, 0.25)];
        MorphogenesisState beforeState = ProductionTestStateFactory.FromRows(
            ["...", "...", "A.."],
            cells,
            contacts,
            SimulationConfiguration.WallCanonical);
        MorphogenesisState afterState = ProductionTestStateFactory.FromRows(
            ["...", "...", "AA."],
            cells,
            contacts,
            SimulationConfiguration.WallCanonical);

        HamiltonianBreakdown before = beforeState.RecomputeHamiltonian();
        MoveEvaluation local = EnergyDeltaCalculator.Evaluate(beforeState, 1, 1);
        HamiltonianBreakdown after = afterState.RecomputeHamiltonian();
        HamiltonianBreakdown recomputedDelta = new(
            after.Contact - before.Contact,
            after.Area - before.Area,
            after.Perimeter - before.Perimeter);

        Assert.Equal(43, before.Contact);
        Assert.Equal(52, after.Contact);
        Assert.Equal(new HamiltonianBreakdown(9, 1, 9), local.Terms);
        Assert.Equal(new HamiltonianBreakdown(9, 1, 9), recomputedDelta);
    }

    [Fact]
    public void Replay_record_preserves_wall_configuration_and_nonzero_J00()
    {
        double[,] contacts =
        {
            { 1, 2 },
            { 2, 0 }
        };
        MorphogenesisState state = ProductionTestStateFactory.FromRows(
            ["...", "...", "A.."],
            [new CellDefinition(1, 1, 1, 1, 8, 0.25)],
            contacts,
            SimulationConfiguration.WallCanonical);
        SerialSimulation simulation = new(state, 0x9009UL, 4);

        SimulationReplayRecord replay = simulation.CreateReplayRecord();
        double[,] replayContacts = replay.ContactEnergies;

        Assert.Equal(BoundaryMode.Wall, replay.BoundaryMode);
        Assert.Equal(LatticeConventions.Canonical, replay.Conventions);
        Assert.Equal(1, replayContacts[0, 0]);
        Assert.Equal(2, replayContacts[0, 1]);
        Assert.Equal(2, replayContacts[1, 0]);
    }

    [Fact]
    public void Final_site_removal_is_rejected_before_energy_or_acceptance()
    {
        int[] ids = new int[25];
        ids[12] = 1;
        MorphogenesisState state = new(5, 5, ids, [new CellDefinition(1, 1, 1, 1, 8, 0.25)], new ContactEnergyMatrix(Contacts));
        ScriptedRandomSource random = new([12, 1]);
        SerialSimulation simulation = new(state, random, 1);
        int[] before = state.GetCellIdsCopy();
        CellState cellBefore = state.GetCellState(1);

        AttemptResult result = simulation.Attempt();

        Assert.Equal(AttemptStatus.Rejected, result.Status);
        Assert.Equal(RejectionReason.FinalSite, result.RejectionReason);
        Assert.Equal(default, result.DeltaH);
        Assert.Null(result.AcceptanceProbability);
        Assert.Null(result.AcceptanceRandomValue);
        Assert.Equal(2, random.IntegerDrawCount);
        Assert.Equal(before, state.GetCellIdsCopy());
        Assert.Equal(cellBefore, state.GetCellState(1));
        random.AssertExhausted();
    }

    private sealed class ScriptedRandomSource(IEnumerable<int> integerValues, IEnumerable<double>? acceptanceValues = null)
        : Rowles.Morphogenesis.Random.IRandomSource
    {
        private readonly Queue<int> _integers = new(integerValues);
        private readonly Queue<double> _acceptance = new(acceptanceValues ?? []);
        private readonly List<int> _integerUpperBounds = [];

        internal int IntegerDrawCount { get; private set; }

        internal int AcceptanceDrawCount { get; private set; }

        internal IReadOnlyList<int> IntegerUpperBounds => _integerUpperBounds;

        internal double[] AcceptanceValuesRemaining => _acceptance.ToArray();

        public int NextInt(int exclusiveUpperBound)
        {
            _integerUpperBounds.Add(exclusiveUpperBound);
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
