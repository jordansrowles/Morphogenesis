using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Initialisation;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Random;
using Rowles.Morphogenesis.Tests.Baseline;
using Rowles.Morphogenesis.Benchmarks.Diagnostics;

namespace Rowles.Morphogenesis.Tests.Dynamics;

public sealed class EntryKernelDifferentialTests
{
    [Theory]
    [InlineData("E00-single-cell-relaxation.json", false, 0, 0)]
    [InlineData("E02-control.json", false, 0, 0)]
    [InlineData("E02-sorting.json", false, 0, 0)]
    [InlineData("E01-fluctuation-high.json", false, 0, 0)]
    [InlineData("E02-control.json", true, 0, 0)]
    [InlineData("E02-sorting-perturbation.json", true, 0, 0)]
    [InlineData("E02-control.json", false, 32, 64)]
    [InlineData("E02-control.json", false, 64, 64)]
    [InlineData("E02-control.json", false, 128, 256)]
    [InlineData("E02-control.json", false, 256, 1024)]
    public void Canonical_attempts_and_logical_draws_match_the_pinned_entry_kernel(string manifestName, bool wall, int width, int cellCount)
    {
        ExperimentManifest manifest = ExperimentManifest.ReadJson(Path.Combine(
            AppContext.BaseDirectory, "experiments", "canonical", manifestName));
        manifest = manifest with { BoundaryMode = wall ? BoundaryMode.Wall : BoundaryMode.Periodic };
        if (width > 0)
        {
            manifest = manifest with
            {
                GridWidth = width,
                GridHeight = width,
                Initialiser = manifest.Initialiser with { CellCount = cellCount }
            };
        }
        foreach (ulong seed in new ulong[] { 17, 123, 20260929 })
        {
            MorphogenesisState baselineState = PackedAggregateInitialiser.Create(manifest, seed).State;
            MorphogenesisState candidateState = PackedAggregateInitialiser.Create(manifest, seed).State;
            MorphogenesisState profiledState = PackedAggregateInitialiser.Create(manifest, seed).State;
            RecordingRandom baselineRandom = new(seed);
            RecordingRandom candidateRandom = new(seed);
            RecordingRandom profiledRandom = new(seed);
            EntrySerialSimulation baseline = new(baselineState, baselineRandom, manifest.FluctuationAmplitude);
            SerialSimulation candidate = new(candidateState, candidateRandom, manifest.FluctuationAmplitude);
            ProfiledSerialSimulation profiled = new(profiledState, profiledRandom, manifest.FluctuationAmplitude);
            int checkedDraws = 0;
            for (int attempt = 0; attempt < 1024; attempt++)
            {
                AttemptResult expected = baseline.Attempt();
                AttemptResult actual = candidate.Attempt();
                AttemptResult diagnostic = profiled.Attempt();
                Assert.Equal(expected, actual);
                Assert.Equal(expected, diagnostic);
                AssertBits(expected.DeltaH.Contact, diagnostic.DeltaH.Contact);
                AssertBits(expected.DeltaH.Area, diagnostic.DeltaH.Area);
                AssertBits(expected.DeltaH.Perimeter, diagnostic.DeltaH.Perimeter);
                AssertBits(expected.AcceptanceProbability, diagnostic.AcceptanceProbability);
                AssertBits(expected.AcceptanceRandomValue, diagnostic.AcceptanceRandomValue);
                AssertBits(expected.DeltaH.Contact, actual.DeltaH.Contact);
                AssertBits(expected.DeltaH.Area, actual.DeltaH.Area);
                AssertBits(expected.DeltaH.Perimeter, actual.DeltaH.Perimeter);
                AssertBits(expected.AcceptanceProbability, actual.AcceptanceProbability);
                AssertBits(expected.AcceptanceRandomValue, actual.AcceptanceRandomValue);
                AssertState(baselineState, candidateState, manifest.Initialiser.CellCount);
                AssertState(baselineState, profiledState, manifest.Initialiser.CellCount);
                Assert.Equal(baseline.AttemptCount, candidate.AttemptCount);
                Assert.Equal(baseline.ConnectivityFallbackCount, candidate.ConnectivityFallbackCount);
                CheckNewDraws();
            }

            for (int mcs = 0; mcs < 2; mcs++)
            {
                McsSummary summary = baseline.RunMcs();
                Assert.Equal(summary, candidate.RunMcs());
                Assert.Equal(summary, profiled.RunMcs());
                Assert.Equal(baseline.CompletedMcs, candidate.CompletedMcs);
                Assert.Equal(baseline.AttemptCount, candidate.AttemptCount);
                AssertState(baselineState, candidateState, manifest.Initialiser.CellCount);
                AssertState(baselineState, profiledState, manifest.Initialiser.CellCount);
                CheckNewDraws();
            }

            void CheckNewDraws()
            {
                Assert.Equal(baselineRandom.Draws.Count, candidateRandom.Draws.Count);
                Assert.Equal(baselineRandom.Draws.Count, profiledRandom.Draws.Count);
                while (checkedDraws < baselineRandom.Draws.Count)
                {
                    Assert.Equal(baselineRandom.Draws[checkedDraws], candidateRandom.Draws[checkedDraws]);
                    Assert.Equal(baselineRandom.Draws[checkedDraws], profiledRandom.Draws[checkedDraws]);
                    checkedDraws++;
                }
            }
        }
    }

    private static void AssertState(MorphogenesisState expected, MorphogenesisState actual, int cellCount)
    {
        Assert.True(expected.Lattice.AsSpan().SequenceEqual(actual.Lattice), "The full lattice differs from the pinned entry trajectory.");
        for (int cellId = 1; cellId <= cellCount; cellId++)
        {
            Assert.Equal(expected.GetCellState(cellId), actual.GetCellState(cellId));
        }
    }

    private static void AssertBits(double expected, double actual) =>
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));

    private static void AssertBits(double? expected, double? actual)
    {
        Assert.Equal(expected.HasValue, actual.HasValue);
        if (expected.HasValue) AssertBits(expected.Value, actual!.Value);
    }

    private readonly record struct Draw(bool Integer, int Bound, long ValueBits);

    private sealed class RecordingRandom(ulong seed) : IRandomSource
    {
        private readonly Xoshiro256StarStar _random = new(seed);
        public List<Draw> Draws { get; } = [];

        public int NextInt(int exclusiveUpperBound)
        {
            int value = _random.NextInt(exclusiveUpperBound);
            Draws.Add(new Draw(true, exclusiveUpperBound, value));
            return value;
        }

        public double NextDouble()
        {
            double value = _random.NextDouble();
            Draws.Add(new Draw(false, 0, BitConverter.DoubleToInt64Bits(value)));
            return value;
        }
    }
}
