using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Diagnostics;
using Rowles.Morphogenesis.Dynamics.Acceleration;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Initialisation;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Random;

namespace Rowles.Morphogenesis.Tests.Dynamics.Acceleration;

public sealed class EventClockSimulationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Checkpoint_partitioning_preserves_events_draws_state_and_counters(bool wall, bool directed)
    {
        ExperimentManifest manifest = ExperimentManifest.ReadJson(Path.Combine(
            AppContext.BaseDirectory, "experiments", "canonical", "E02-sorting.json"));
        manifest = manifest with { BoundaryMode = wall ? BoundaryMode.Wall : BoundaryMode.Periodic };
        ProposalSpaceKind kind = directed ? ProposalSpaceKind.DirectedInterface : ProposalSpaceKind.BorderSites;
        var firstState = PackedAggregateInitialiser.Create(manifest, 17).State;
        var secondState = PackedAggregateInitialiser.Create(manifest, 17).State;
        RecordingRandom firstRandom = new(123);
        RecordingRandom secondRandom = new(123);
        EventClockSimulation first = new(firstState, firstRandom, manifest.FluctuationAmplitude, kind);
        EventClockSimulation second = new(secondState, secondRandom, manifest.FluctuationAmplitude, kind);
        McsSummary summary = first.Advance(10000);
        int accepted = 0, rejected = 0, noOps = 0, fallbacks = 0;
        for (int slot = 0; slot < 10000; slot++)
        {
            McsSummary step = second.Advance(1);
            accepted += step.Accepted; rejected += step.Rejected; noOps += step.NoOps; fallbacks += step.ConnectivityFallbacks;
        }
        Assert.Equal(summary, new McsSummary(10000, accepted, rejected, noOps, fallbacks));
        Assert.Equal(firstState.GetCellIdsCopy(), secondState.GetCellIdsCopy());
        for (int id = 1; id <= manifest.Initialiser.CellCount; id++) Assert.Equal(firstState.GetCellState(id), secondState.GetCellState(id));
        Assert.Equal(firstRandom.Draws, secondRandom.Draws);
        Assert.Equal(first.RawEventProposals, second.RawEventProposals);
        Assert.Equal(first.CanonicalEquivalentAttempts - first.RawEventProposals, first.SkippedNoOpAttempts);
        Assert.Equal(first.NoOpProposals - first.SkippedNoOpAttempts, first.RetainedNoOpEvents);
        Assert.Equal(summary.Rejected, first.RejectedProposals);
        Assert.Equal(0, first.FailedEventProposals);
        Assert.Equal(first.CanonicalEquivalentAttempts, second.CanonicalEquivalentAttempts);
        Assert.Equal(first.AcceptedCopies, second.AcceptedCopies);
        Assert.Equal(first.NoOpProposals, second.NoOpProposals);
        Assert.Equal(first.HardConstraintRejections, second.HardConstraintRejections);
        Assert.Equal(first.EnergyRejections, second.EnergyRejections);
        Assert.Equal(first.FixedWallRejections, second.FixedWallRejections);
        Assert.Equal(first.BorderSiteCount, second.BorderSiteCount);
        Assert.Equal(first.DirectedInterfaceCount, second.DirectedInterfaceCount);
        Assert.Equal(10000, first.CanonicalEquivalentAttempts);
        Assert.Equal(10000, first.AcceptedCopies + first.NoOpProposals + first.HardConstraintRejections + first.EnergyRejections + first.FixedWallRejections);
        InvariantValidator.Validate(firstState);
    }

    [Fact]
    public void Geometric_sampler_handles_empty_full_and_inverse_cdf_boundaries()
    {
        ValueRandom noDraw = new(double.NaN);
        Assert.Equal(long.MaxValue, GeometricWaitingTime.Sample(0, 4, noDraw));
        Assert.Equal(0, GeometricWaitingTime.Sample(4, 4, noDraw));
        Assert.Equal(0, GeometricWaitingTime.Sample(1, 2, new ValueRandom(0)));
        Assert.Equal(1, GeometricWaitingTime.Sample(1, 2, new ValueRandom(0.5)));
        Assert.Equal(2, GeometricWaitingTime.Sample(1, 2, new ValueRandom(0.75)));
        Assert.Throws<InvalidOperationException>(() => GeometricWaitingTime.Sample(1, 2, new ValueRandom(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => GeometricWaitingTime.Sample(5, 4, noDraw));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Homogeneous_tissue_skips_no_ops_and_preserves_fixed_wall_rejections(bool wall, bool directed)
    {
        MorphogenesisState state = new(5, 5, Enumerable.Repeat(1, 25).ToArray(),
            [new CellDefinition(1, 1, 25, 1, wall ? 20 : 0, 1)],
            new ContactEnergyMatrix(new double[,] { { 0, 1 }, { 1, 1 } }),
            wall ? SimulationConfiguration.WallCanonical : SimulationConfiguration.PeriodicCanonical);
        EventClockSimulation simulation = new(state, 17, 12,
            directed ? ProposalSpaceKind.DirectedInterface : ProposalSpaceKind.BorderSites);
        McsSummary summary = simulation.Advance(10000);
        Assert.Equal(0, summary.Accepted);
        Assert.Equal(10000, summary.NoOps + summary.Rejected);
        Assert.Equal(summary.Rejected, simulation.FixedWallRejections);
        Assert.Equal(25, state.GetCellState(1).Area);
        if (wall) Assert.True(simulation.FixedWallRejections > 0);
        else { Assert.Equal(0, simulation.RawEventProposals); Assert.Equal(10000, summary.NoOps); }
        InvariantValidator.Validate(state);
    }

    [Fact]
    public void Failed_acceptance_draw_still_counts_its_event_slot_without_negative_skipped_counters()
    {
        ExperimentManifest manifest = ExperimentManifest.ReadJson(Path.Combine(
            AppContext.BaseDirectory, "experiments", "canonical", "E02-sorting.json"));
        MorphogenesisState state = PackedAggregateInitialiser.Create(manifest, 17).State;
        InterfaceIndex index = new(state);
        int uphillPosition = -1;
        for (int position = 0; position < index.DirectedProposals.Count; position++)
        {
            int edge = index.DirectedProposals.At(position);
            int target = edge / index.Degree;
            int source = index.Resolve(target, edge % index.Degree);
            if (source >= 0 && state.Lattice[target] == 0 && state.Lattice[source] > 0 &&
                EnergyDeltaCalculator.Evaluate(state, target, state.Lattice[source]).Total > 0)
            {
                uphillPosition = position;
                break;
            }
        }
        Assert.True(uphillPosition >= 0);
        EventClockSimulation simulation = new(state, new InvalidAcceptanceRandom(uphillPosition),
            manifest.FluctuationAmplitude, ProposalSpaceKind.DirectedInterface);
        Assert.Throws<InvalidOperationException>(() => simulation.Advance(1));
        Assert.Equal(1, simulation.CanonicalEquivalentAttempts);
        Assert.Equal(1, simulation.RawEventProposals);
        Assert.Equal(0, simulation.SkippedNoOpAttempts);
        Assert.Equal(1, simulation.FailedEventProposals);
        InvariantValidator.Validate(state);
    }

    private sealed class InvalidAcceptanceRandom(int position) : IRandomSource
    {
        private int _doubleCalls;
        public int NextInt(int exclusiveUpperBound)
        {
            Assert.InRange(position, 0, exclusiveUpperBound - 1);
            return position;
        }
        public double NextDouble() => ++_doubleCalls == 1 ? 0 : double.NaN;
    }

    private sealed class RecordingRandom(ulong seed) : IRandomSource
    {
        private readonly Xoshiro256StarStar _random = new(seed);
        internal List<(int Bound, long Value)> Draws { get; } = [];
        public int NextInt(int exclusiveUpperBound)
        {
            int value = _random.NextInt(exclusiveUpperBound);
            Draws.Add((exclusiveUpperBound, value));
            return value;
        }
        public double NextDouble()
        {
            double value = _random.NextDouble();
            Draws.Add((0, BitConverter.DoubleToInt64Bits(value)));
            return value;
        }
    }

    private sealed class ValueRandom(double value) : IRandomSource
    {
        public int NextInt(int exclusiveUpperBound) => throw new InvalidOperationException();
        public double NextDouble() => value;
    }
}
