using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Random;
using Rowles.Morphogenesis.Tests.Fixtures;
using Xunit.Abstractions;

namespace Rowles.Morphogenesis.Tests.Dynamics;

public sealed class SerialSimulationTests(ITestOutputHelper output)
{
    private static readonly double[,] Contacts = { { 0, 2 }, { 2, 0 } };

    [Fact]
    public void Equal_seed_and_configuration_replay_the_same_serial_trajectory()
    {
        string[] rows = ["..........", "..........", "...AA.....", "...AA.....", "..........", "..........", "..........", "..........", "..........", ".........."];
        CellDefinition[] cells = [new(1, 1, 4, 1, 20, 0.08)];
        MorphogenesisState firstState = ProductionTestStateFactory.FromRows(rows, cells, Contacts);
        MorphogenesisState secondState = ProductionTestStateFactory.FromRows(rows, cells, Contacts);
        SerialSimulation first = new(firstState, new Xoshiro256StarStar(0xD15EA5EUL), 6);
        SerialSimulation second = new(secondState, new Xoshiro256StarStar(0xD15EA5EUL), 6);

        for (int attempt = 0; attempt < 2_000; attempt++)
        {
            Assert.Equal(first.Attempt(), second.Attempt());
        }

        Assert.Equal(firstState.GetCellIdsCopy(), secondState.GetCellIdsCopy());
        Assert.Equal(firstState.GetCellState(1), secondState.GetCellState(1));
        Assert.Equal(first.ConnectivityFallbackCount, second.ConnectivityFallbackCount);
        firstState.ValidateInvariants();
        secondState.ValidateInvariants();
    }

    [Fact]
    public void Single_cell_relaxation_matrix_keeps_area_controlled_and_geometry_exact()
    {
        BoundaryMode[] boundaries = [BoundaryMode.Periodic, BoundaryMode.Wall];
        double[] temperatures = [2, 8];
        double[] areaStiffnesses = [0.75, 1.5];
        int run = 0;
        foreach (BoundaryMode boundary in boundaries)
        {
            foreach (double temperature in temperatures)
            {
                foreach (double areaStiffness in areaStiffnesses)
                {
                    int width = 20;
                    int height = 20;
                    int[] ids = new int[width * height];
                    for (int x = 2; x < 18; x++)
                    {
                        ids[10 * width + x] = 1;
                    }

                    CellDefinition cell = new(1, 1, 16, areaStiffness, 32, 0.12);
                    MorphogenesisState state = new(
                        width,
                        height,
                        ids,
                        [cell],
                        new ContactEnergyMatrix(Contacts),
                        new SimulationConfiguration(boundary, LatticeConventions.Canonical));
                    int initialPerimeter = state.GetCellState(1).Perimeter;
                    SerialSimulation simulation = new(state, new Xoshiro256StarStar((ulong)(0xC0FFEE + run)), temperature);
                    int accepted = 0;
                    int rejected = 0;
                    for (int mcs = 0; mcs < 40; mcs++)
                    {
                        McsSummary summary = simulation.RunMcs();
                        accepted += summary.Accepted;
                        rejected += summary.Rejected;
                        state.ValidateInvariants();
                    }

                    CellState finalCell = state.GetCellState(1);
                    Assert.InRange(finalCell.Area, 6, 28);
                    Assert.True(finalCell.Perimeter < initialPerimeter,
                        $"{boundary}, T={temperature}, lambdaArea={areaStiffness}: perimeter {initialPerimeter} -> {finalCell.Perimeter}.");
                    Assert.InRange(Math.Abs(finalCell.Perimeter - 32), 0, 32);
                    Assert.True(finalCell.IsAlive);
                    output.WriteLine(
                        $"run={run} boundary={boundary} T={temperature} lambdaArea={areaStiffness} " +
                        $"area={finalCell.Area} perimeter={initialPerimeter}->{finalCell.Perimeter} " +
                        $"accepted={accepted} rejected={rejected} globalFallbacks={simulation.ConnectivityFallbackCount}");
                    run++;
                }
            }
        }

        Assert.Equal(8, run);
    }

    [Fact]
    public void Steady_state_attempt_loop_has_no_avoidable_managed_allocations()
    {
        const int SmallAttemptCount = 10_000;
        const int LargeAttemptCount = 100_000;
        const int SampleCount = 3;

        SerialSimulation sacrificial = CreateAllocationMeasurementSimulation();
        WarmAttemptLoop(sacrificial, 500);
        _ = MeasureAttemptAllocations(sacrificial, LargeAttemptCount);
        sacrificial.State.ValidateInvariants();

        long smallMinimum = MeasureMinimumAttemptAllocations(SmallAttemptCount, SampleCount);
        long largeMinimum = MeasureMinimumAttemptAllocations(LargeAttemptCount, SampleCount);

        // The Ubuntu runner has reported a fixed 24-byte one-off after warm-up. Compare
        // allocation growth instead of requiring an absolute zero: a fixed measurement/runtime
        // overhead stays constant while any allocation in every Attempt() scales by 10x here.
        Assert.Equal(smallMinimum, largeMinimum);
    }

    private long MeasureMinimumAttemptAllocations(int attemptCount, int sampleCount)
    {
        long minimum = long.MaxValue;
        for (int sample = 0; sample < sampleCount; sample++)
        {
            SerialSimulation simulation = CreateAllocationMeasurementSimulation();
            WarmAttemptLoop(simulation, 500);
            long allocated = MeasureAttemptAllocations(simulation, attemptCount);
            minimum = Math.Min(minimum, allocated);
            output.WriteLine($"allocation sample={sample} attempts={attemptCount} bytes={allocated}");
            simulation.State.ValidateInvariants();
        }

        return minimum;
    }

    private static long MeasureAttemptAllocations(SerialSimulation simulation, int attemptCount)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int attempt = 0; attempt < attemptCount; attempt++)
        {
            simulation.Attempt();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void WarmAttemptLoop(SerialSimulation simulation, int attemptCount)
    {
        for (int attempt = 0; attempt < attemptCount; attempt++)
        {
            simulation.Attempt();
        }
    }

    private static SerialSimulation CreateAllocationMeasurementSimulation()
    {
        const int Width = 20;
        const int Height = 20;
        int[] ids = new int[Width * Height];
        for (int y = 8; y < 12; y++)
        {
            for (int x = 8; x < 12; x++)
            {
                ids[y * Width + x] = 1;
            }
        }

        MorphogenesisState state = new(
            Width,
            Height,
            ids,
            [new CellDefinition(1, 1, 16, 1, 32, 0.1)],
            new ContactEnergyMatrix(Contacts));
        return new SerialSimulation(state, new Xoshiro256StarStar(0xA110CUL), 4);
    }

    [Fact]
    public void Replay_record_reconstructs_full_seeded_run_from_initial_state()
    {
        const ulong Seed = 0x1234ABCDUL;
        string[] rows =
        [
            "..........",
            "..........",
            "..AAA.....",
            "..AAA.BB..",
            "......BB..",
            "..........",
            "..........",
            "..........",
            "..........",
            ".........."
        ];
        CellDefinition[] cells =
        [
            new(1, 1, 6, 0.8, 24, 0.08),
            new(3, 1, 4, 0.8, 18, 0.08)
        ];

        MorphogenesisState originalState = ProductionTestStateFactory.FromRows(rows, cells, Contacts);
        int[] initialIds = originalState.GetCellIdsCopy();
        SerialSimulation original = new(originalState, Seed, 6);

        for (int attempt = 0; attempt < 37; attempt++)
        {
            original.Attempt();
        }

        original.RunMcs();

        for (int attempt = 0; attempt < 63; attempt++)
        {
            original.Attempt();
        }

        SimulationReplayRecord record = original.CreateReplayRecord();
        int[] originalFinalIds = originalState.GetCellIdsCopy();
        CellState originalCell1 = originalState.GetCellState(1);
        CellState originalCell3 = originalState.GetCellState(3);
        long originalFallbacks = original.ConnectivityFallbackCount;

        Assert.Equal(initialIds, record.CellIds);
        Assert.NotEqual(initialIds, originalFinalIds);
        Assert.Equal(SimulationReplayRecord.CurrentReplayFormatVersion, record.ReplayFormatVersion);
        Assert.Equal(SimulationReplayRecord.SerialKernelVersion, record.KernelVersion);
        Assert.Equal(Xoshiro256StarStar.AlgorithmName, record.RandomAlgorithm);
        Assert.Equal(Seed, record.Seed);
        Assert.Equal(200, record.AttemptCount);
        Assert.Equal(1, record.CompletedMcs);

        SerialSimulation expected = new(
            ProductionTestStateFactory.FromRows(rows, cells, Contacts),
            Seed,
            6);
        SerialSimulation replay = SerialSimulation.FromReplayRecord(record);

        Assert.Equal(0, replay.AttemptCount);
        Assert.Equal(0, replay.CompletedMcs);

        for (long attempt = 0; attempt < record.AttemptCount; attempt++)
        {
            Assert.Equal(expected.Attempt(), replay.Attempt());
        }

        Assert.Equal(originalFinalIds, replay.State.GetCellIdsCopy());
        Assert.Equal(originalCell1, replay.State.GetCellState(1));
        Assert.Equal(originalCell3, replay.State.GetCellState(3));
        Assert.Equal(originalFallbacks, replay.ConnectivityFallbackCount);
        Assert.Equal(expected.ConnectivityFallbackCount, replay.ConnectivityFallbackCount);
        replay.State.ValidateInvariants();
    }

    [Fact]
    public void Replay_record_owns_defensive_copies_of_initial_inputs()
    {
        int[] ids = new int[25];
        ids[12] = 1;
        MorphogenesisState state = new(
            5,
            5,
            ids,
            [new CellDefinition(1, 1, 1, 0.5, 8, 0.1)],
            new ContactEnergyMatrix(Contacts),
            SimulationConfiguration.WallCanonical);
        SerialSimulation simulation = new(state, 0x55AAUL, 3.5);
        SimulationReplayRecord record = simulation.CreateReplayRecord();

        int[] firstIds = record.CellIds;
        CellReplayParameters[] firstCells = record.Cells;
        double[,] firstContacts = record.ContactEnergies;

        firstIds[12] = 0;
        firstCells[0] = default;
        firstContacts[0, 1] = 999;

        Assert.Equal(1, record.CellIds[12]);
        Assert.Equal(new CellReplayParameters(1, 1, 1, 0.5, 8, 0.1), Assert.Single(record.Cells));
        Assert.Equal(2, record.ContactEnergies[0, 1]);
    }

    [Fact]
    public void Replay_record_rejects_custom_random_sources()
    {
        int[] ids = new int[25];
        ids[12] = 1;
        MorphogenesisState state = new(
            5,
            5,
            ids,
            [new CellDefinition(1, 1, 1, 0.5, 8, 0.1)],
            new ContactEnergyMatrix(Contacts));
        SerialSimulation simulation = new(state, new ConstantRandomSource(), 3.5);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(simulation.CreateReplayRecord);
        Assert.Contains("seeded xoshiro256", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Replay_record_keeps_initial_lattice_after_the_live_state_changes()
    {
        string[] rows =
        [
            "..........",
            "..........",
            "...AAAA...",
            "...AAAA...",
            "..........",
            "..........",
            "..........",
            "..........",
            "..........",
            ".........."
        ];
        CellDefinition[] cells = [new(1, 1, 8, 0.5, 24, 0.05)];
        MorphogenesisState state = ProductionTestStateFactory.FromRows(rows, cells, Contacts);
        int[] initial = state.GetCellIdsCopy();
        SerialSimulation simulation = new(state, 0xBEEFUL, 8);

        int attempts = 0;
        while (attempts < 10_000 && state.GetCellIdsCopy().SequenceEqual(initial))
        {
            simulation.Attempt();
            attempts++;
        }

        Assert.False(state.GetCellIdsCopy().SequenceEqual(initial));
        SimulationReplayRecord record = simulation.CreateReplayRecord();
        Assert.Equal(initial, record.CellIds);

        SerialSimulation replay = SerialSimulation.FromReplayRecord(record);
        for (long attempt = 0; attempt < record.AttemptCount; attempt++)
        {
            replay.Attempt();
        }

        Assert.Equal(state.GetCellIdsCopy(), replay.State.GetCellIdsCopy());
    }

    private sealed class ConstantRandomSource : IRandomSource
    {
        public int NextInt(int exclusiveUpperBound) => 0;

        public double NextDouble() => 0;
    }

}
