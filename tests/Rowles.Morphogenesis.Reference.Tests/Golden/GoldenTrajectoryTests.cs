using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rowles.Morphogenesis.Reference.Dynamics;
using Rowles.Morphogenesis.Reference.Energy;
using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Model;
using Rowles.Morphogenesis.Reference.Random;
using Rowles.Morphogenesis.Reference.Tests.Fixtures;

namespace Rowles.Morphogenesis.Reference.Tests.Golden;

public sealed class GoldenTrajectoryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public void G10_seeded_trajectory_replays_every_proposal_delta_decision_and_state()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "G10.golden.json");
        GoldenTrajectory fixture = JsonSerializer.Deserialize<GoldenTrajectory>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Could not read G10 fixture at {path}.");
        double[,] contacts = ToMatrix(fixture.ContactEnergies);
        Dictionary<int, int> types = fixture.Cells.ToDictionary(cell => cell.CellId, cell => cell.CellTypeId);
        ReferenceState state = TestStateFactory.FromRows(fixture.InitialRows, types, fixture.Cells, contacts);
        ulong seed = ulong.Parse(fixture.Seed.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        ReferenceSimulation simulation = new(state, new Xoshiro256StarStar(seed), fixture.FluctuationAmplitude);

        Assert.Equal("xoshiro256** with SplitMix64", fixture.RandomAlgorithm);
        Assert.Equal(
            ["target-site index", "copy-neighbour index", "acceptance value only for positive DeltaH"],
            fixture.RandomDrawOrder);
        Assert.Equal(fixture.Attempts.Length, fixture.Steps);
        for (int index = 0; index < fixture.Attempts.Length; index++)
        {
            GoldenAttempt expected = fixture.Attempts[index];
            AttemptResult actual = simulation.Attempt();

            Assert.Equal(index, expected.Index);
            Assert.Equal(expected.Target, actual.Target);
            Assert.Equal(expected.Source, actual.Source);
            Assert.Equal(expected.OldCellId, actual.OldCellId);
            Assert.Equal(expected.NewCellId, actual.NewCellId);
            Assert.Equal(expected.Status, actual.Status.ToString());
            AssertEnergy(expected.DeltaH, actual.DeltaH);
            AssertNullableEnergy(expected.AcceptanceProbability, actual.AcceptanceProbability);
            AssertNullableEnergy(expected.AcceptanceRandomValue, actual.AcceptanceRandomValue);
            Assert.Equal(expected.RowsAfter, ToRows(state));
        }

        Assert.Equal(fixture.FinalRows, ToRows(state));
        AssertEnergy(fixture.FinalHamiltonian, ReferenceEnergy.ComputeHamiltonian(state));
    }

    private static void AssertEnergy(HamiltonianBreakdown expected, HamiltonianBreakdown actual)
    {
        Assert.True(EnergyComparison.NearlyEqual(expected.Contact, actual.Contact));
        Assert.True(EnergyComparison.NearlyEqual(expected.Area, actual.Area));
        Assert.True(EnergyComparison.NearlyEqual(expected.Perimeter, actual.Perimeter));
        Assert.True(EnergyComparison.NearlyEqual(expected.Total, actual.Total));
    }

    private static void AssertNullableEnergy(double? expected, double? actual)
    {
        Assert.Equal(expected.HasValue, actual.HasValue);
        if (expected.HasValue && actual.HasValue)
        {
            Assert.True(EnergyComparison.NearlyEqual(expected.Value, actual.Value), $"Expected {expected:R}, actual {actual:R}.");
        }
    }

    private static double[,] ToMatrix(double[][] rows)
    {
        if (rows.Length == 0 || rows.Any(row => row.Length != rows.Length))
        {
            throw new InvalidDataException("G10 contact matrix must be square.");
        }

        double[,] result = new double[rows.Length, rows.Length];
        for (int row = 0; row < rows.Length; row++)
        {
            for (int column = 0; column < rows.Length; column++)
            {
                result[row, column] = rows[row][column];
            }
        }

        return result;
    }

    private static string[] ToRows(ReferenceState state)
    {
        string[] rows = new string[state.Height];
        for (int row = 0; row < state.Height; row++)
        {
            int y = state.Height - 1 - row;
            char[] cells = new char[state.Width];
            for (int x = 0; x < state.Width; x++)
            {
                cells[x] = state.CellIdAt(x, y) switch
                {
                    0 => '.',
                    1 => 'A',
                    2 => 'a',
                    3 => 'B',
                    _ => throw new InvalidDataException("Unexpected cell ID in G10 state.")
                };
            }

            rows[row] = new string(cells);
        }

        return rows;
    }

    private sealed record GoldenTrajectory(
        string RandomAlgorithm,
        string Seed,
        double FluctuationAmplitude,
        string[] RandomDrawOrder,
        int Steps,
        string[] InitialRows,
        CellDefinition[] Cells,
        double[][] ContactEnergies,
        GoldenAttempt[] Attempts,
        string[] FinalRows,
        HamiltonianBreakdown FinalHamiltonian);

    private sealed record GoldenAttempt(
        int Index,
        GridPoint Target,
        GridPoint Source,
        int OldCellId,
        int NewCellId,
        string Status,
        HamiltonianBreakdown DeltaH,
        double? AcceptanceProbability,
        double? AcceptanceRandomValue,
        string[] RowsAfter);
}
