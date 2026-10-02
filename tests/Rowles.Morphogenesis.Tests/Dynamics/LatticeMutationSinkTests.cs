using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Dynamics.Acceleration;
using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Execution;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Random;
using Rowles.Morphogenesis.Tests.Experiments;

namespace Rowles.Morphogenesis.Tests.Dynamics;

public sealed class LatticeMutationSinkTests
{
    [Theory]
    [InlineData(RejectionReason.FixedWall)]
    [InlineData(RejectionReason.FinalSite)]
    [InlineData(RejectionReason.Disconnected)]
    [InlineData(RejectionReason.Metropolis)]
    public void Rejections_do_not_invoke_the_sink(RejectionReason expectedReason)
    {
        (MorphogenesisState state, int target, int direction, double fluctuation) = CreateRejectedAttempt(expectedReason);
        RecordingSink sink = new(state);
        SerialSimulation simulation = new(state, new SequenceRandom(target, direction, 0.5), fluctuation, sink);

        AttemptResult result = simulation.Attempt();

        Assert.Equal(AttemptStatus.Rejected, result.Status);
        Assert.Equal(expectedReason, result.RejectionReason);
        Assert.Empty(sink.Copies);
    }

    [Fact]
    public void Same_id_no_op_does_not_invoke_the_sink()
    {
        MorphogenesisState state = EmptyWallState(5, 5);
        RecordingSink sink = new(state);
        SerialSimulation simulation = new(state, new SequenceRandom(12, 0, 0), 0, sink);

        AttemptResult result = simulation.Attempt();

        Assert.Equal(AttemptStatus.NoOp, result.Status);
        Assert.Empty(sink.Copies);
    }

    [Fact]
    public void Accepted_copy_notifies_once_after_lattice_and_geometry_are_committed()
    {
        MorphogenesisState state = SingleCellGrowthState();
        RecordingSink sink = new(state, validateInvariants: true);
        SerialSimulation simulation = new(state, new SequenceRandom(13, 3, 0), 1_000, sink);

        AttemptResult result = simulation.Attempt();

        Assert.Equal(AttemptStatus.Accepted, result.Status);
        Assert.Equal([(13, 0, 1)], sink.Copies);
        Assert.Equal(2, state.GetCellState(1).Area);
    }

    [Fact]
    public void Passive_sink_preserves_seeded_trajectory_and_counters()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create();
        ExperimentSimulationInstance withoutSinkInitial = ExperimentSimulationFactory.Create(manifest, 0);
        ExperimentSimulationInstance withSinkInitial = ExperimentSimulationFactory.Create(manifest, 0);
        RecordingSink sink = new(withSinkInitial.Initialisation.State);
        SerialSimulation withoutSink = withoutSinkInitial.Simulation;
        SerialSimulation withSink = new(
            withSinkInitial.Initialisation.State,
            new Xoshiro256StarStar(withSinkInitial.DynamicsSeed),
            manifest.FluctuationAmplitude,
            sink);
        int accepted = 0;

        for (int attempt = 0; attempt < 4_096; attempt++)
        {
            int callbacksBefore = sink.Copies.Count;
            AttemptResult expected = withoutSink.Attempt();
            AttemptResult actual = withSink.Attempt();
            Assert.Equal(expected, actual);
            if (actual.Status == AttemptStatus.Accepted)
            {
                accepted++;
                Assert.Equal(callbacksBefore + 1, sink.Copies.Count);
            }
            else
            {
                Assert.Equal(callbacksBefore, sink.Copies.Count);
            }
        }

        for (int mcs = 0; mcs < 3; mcs++)
        {
            McsSummary expected = withoutSink.RunMcs();
            McsSummary actual = withSink.RunMcs();
            Assert.Equal(expected, actual);
            accepted += actual.Accepted;
        }

        Assert.True(accepted > 0);
        Assert.Equal(accepted, sink.Copies.Count);
        Assert.Equal(withoutSink.State.GetCellIdsCopy(), withSink.State.GetCellIdsCopy());
        Assert.Equal(withoutSink.AttemptCount, withSink.AttemptCount);
        Assert.Equal(withoutSink.CompletedMcs, withSink.CompletedMcs);
        Assert.Equal(withoutSink.ConnectivityFallbackCount, withSink.ConnectivityFallbackCount);
    }

    [Fact]
    public void Sink_exception_propagates_after_the_accepted_copy_is_committed()
    {
        MorphogenesisState state = SingleCellGrowthState();
        ThrowingSink sink = new(state);
        SerialSimulation simulation = new(state, new SequenceRandom(13, 3, 0), 1_000, sink);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => simulation.Attempt());

        Assert.Equal("sink failed", error.Message);
        Assert.Equal(1, state.CellIdAt(13));
        Assert.Equal(2, state.GetCellState(1).Area);
        Assert.Equal(1, simulation.AttemptCount);
        state.ValidateInvariants();
    }

    [Fact]
    public void Event_clock_forwards_each_accepted_copy_once()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create();
        ExperimentSimulationInstance instance = ExperimentSimulationFactory.Create(manifest, 1);
        RecordingSink sink = new(instance.Initialisation.State);
        EventClockSimulation simulation = new(
            instance.Initialisation.State,
            new Xoshiro256StarStar(instance.DynamicsSeed),
            manifest.FluctuationAmplitude,
            ProposalSpaceKind.DirectedInterface,
            sink);

        McsSummary summary = simulation.Advance(20_000);

        Assert.Equal(summary.Accepted, sink.Copies.Count);
        Assert.Equal(simulation.AcceptedCopies, sink.Copies.Count);
        Assert.NotEmpty(sink.Copies);
    }

    private static (MorphogenesisState State, int Target, int Direction, double Fluctuation) CreateRejectedAttempt(
        RejectionReason reason)
    {
        switch (reason)
        {
            case RejectionReason.FixedWall:
                return (EmptyWallState(3, 3), 0, 0, 0);
            case RejectionReason.FinalSite:
            {
                int[] ids = new int[25];
                ids[12] = 1;
                return (CreateState(5, 5, ids, [new CellDefinition(1, 1, 1, 0, 4, 0)],
                    new double[,] { { 0, 0 }, { 0, 0 } }), 12, 0, 1);
            }
            case RejectionReason.Disconnected:
            {
                int[] ids = new int[25];
                ids[11] = 1;
                ids[12] = 1;
                ids[13] = 1;
                return (CreateState(5, 5, ids, [new CellDefinition(1, 1, 3, 0, 8, 0)],
                    new double[,] { { 0, 0 }, { 0, 0 } }), 12, 0, 1);
            }
            case RejectionReason.Metropolis:
            {
                MorphogenesisState state = SingleCellGrowthState();
                return (state, 13, 3, 0);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(reason));
        }
    }

    private static MorphogenesisState SingleCellGrowthState()
    {
        int[] ids = new int[25];
        ids[12] = 1;
        return CreateState(5, 5, ids, [new CellDefinition(1, 1, 1, 0, 4, 0)],
            new double[,] { { 0, 100 }, { 100, 0 } });
    }

    private static MorphogenesisState EmptyWallState(int width, int height) =>
        CreateState(width, height, new int[width * height], [], new double[,] { { 0 } });

    private static MorphogenesisState CreateState(
        int width,
        int height,
        int[] ids,
        CellDefinition[] cells,
        double[,] contacts) => new(
            width,
            height,
            ids,
            cells,
            new ContactEnergyMatrix(contacts),
            SimulationConfiguration.WallCanonical);

    private sealed class RecordingSink(MorphogenesisState state, bool validateInvariants = false) : ILatticeMutationSink
    {
        internal List<(int TargetIndex, int OldCellId, int NewCellId)> Copies { get; } = [];

        public void AcceptedCopy(int targetIndex, int oldCellId, int newCellId)
        {
            Assert.Equal(newCellId, state.CellIdAt(targetIndex));
            if (validateInvariants)
            {
                state.ValidateInvariants();
            }

            Copies.Add((targetIndex, oldCellId, newCellId));
        }
    }

    private sealed class ThrowingSink(MorphogenesisState state) : ILatticeMutationSink
    {
        public void AcceptedCopy(int targetIndex, int oldCellId, int newCellId)
        {
            Assert.Equal(newCellId, state.CellIdAt(targetIndex));
            throw new InvalidOperationException("sink failed");
        }
    }

    private sealed class SequenceRandom(int target, int direction, double acceptance) : IRandomSource
    {
        private int _integerDraws;

        public int NextInt(int exclusiveUpperBound)
        {
            int value = _integerDraws++ switch
            {
                0 => target,
                1 => direction,
                _ => throw new InvalidOperationException("Unexpected integer draw.")
            };
            Assert.InRange(value, 0, exclusiveUpperBound - 1);
            return value;
        }

        public double NextDouble() => acceptance;
    }
}
