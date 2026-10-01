using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Tests.Fixtures;
using ReferenceRandom = Rowles.Morphogenesis.Reference.Random.Xoshiro256StarStar;
using ReferenceSimulation = Rowles.Morphogenesis.Reference.Dynamics.ReferenceSimulation;

namespace Rowles.Morphogenesis.Tests.Golden;

public sealed class GoldenTrajectoryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public void G10_replays_each_periodic_proposal_energy_draw_decision_and_intermediate_state()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "G10.golden.json");
        GoldenFixture fixture = JsonSerializer.Deserialize<GoldenFixture>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Could not read G10 fixture at {path}.");
        CellDefinition[] cells = fixture.Cells.Select(cell => new CellDefinition(
            cell.CellId,
            cell.CellTypeId,
            cell.TargetArea,
            cell.AreaStiffness,
            cell.TargetPerimeter,
            cell.PerimeterStiffness)).ToArray();
        double[,] contacts = ToMatrix(fixture.ContactEnergies);
        MorphogenesisState productionState = ProductionTestStateFactory.FromRows(fixture.InitialRows, cells, contacts);
        Rowles.Morphogenesis.Reference.Model.ReferenceState referenceState =
            ProductionTestStateFactory.ToReference(fixture.InitialRows, cells, contacts);
        ulong seed = ulong.Parse(fixture.Seed.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        SerialSimulation production = new(productionState, seed, fixture.FluctuationAmplitude);
        ReferenceSimulation reference = new(referenceState, new ReferenceRandom(seed), fixture.FluctuationAmplitude);

        Assert.Equal("xoshiro256** with SplitMix64", fixture.RandomAlgorithm);
        Assert.Equal(fixture.Attempts.Length, fixture.Steps);
        for (int index = 0; index < fixture.Attempts.Length; index++)
        {
            GoldenAttempt expected = fixture.Attempts[index];
            AttemptResult actual = production.Attempt();
            Rowles.Morphogenesis.Reference.Dynamics.AttemptResult referenceAttempt = reference.Attempt();

            Assert.Equal(index, expected.Index);
            Assert.Equal(expected.Target.X, actual.TargetIndex % productionState.Width);
            Assert.Equal(expected.Target.Y, actual.TargetIndex / productionState.Width);
            Assert.Equal(expected.Source.X, actual.SourceIndex % productionState.Width);
            Assert.Equal(expected.Source.Y, actual.SourceIndex / productionState.Width);
            Assert.Equal(expected.Target.X, referenceAttempt.Target.X);
            Assert.Equal(expected.Target.Y, referenceAttempt.Target.Y);
            Assert.Equal(expected.Source.X, referenceAttempt.Source.X);
            Assert.Equal(expected.Source.Y, referenceAttempt.Source.Y);
            Assert.Equal(expected.OldCellId, actual.OldCellId);
            Assert.Equal(expected.NewCellId, actual.NewCellId);
            Assert.Equal(expected.Status, actual.Status.ToString());
            Assert.Equal(expected.Status, referenceAttempt.Status.ToString());
            AssertEnergy(expected.DeltaH, actual.DeltaH);
            AssertEnergy(expected.DeltaH, new HamiltonianBreakdown(
                referenceAttempt.DeltaH.Contact,
                referenceAttempt.DeltaH.Area,
                referenceAttempt.DeltaH.Perimeter));
            AssertNullableClose(expected.AcceptanceProbability, actual.AcceptanceProbability);
            AssertNullableClose(expected.AcceptanceRandomValue, actual.AcceptanceRandomValue);
            AssertNullableClose(expected.AcceptanceProbability, referenceAttempt.AcceptanceProbability);
            AssertNullableClose(expected.AcceptanceRandomValue, referenceAttempt.AcceptanceRandomValue);
            Assert.Equal(expected.RowsAfter, ProductionTestStateFactory.ToRows(productionState));
            Assert.Equal(expected.RowsAfter, ToRows(referenceState));
        }

        Assert.Equal(fixture.FinalRows, ProductionTestStateFactory.ToRows(productionState));
        AssertEnergy(fixture.FinalHamiltonian, productionState.RecomputeHamiltonian());
        productionState.ValidateInvariants();
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

    private static string[] ToRows(Rowles.Morphogenesis.Reference.Model.ReferenceState state)
    {
        string[] rows = new string[state.Height];
        for (int row = 0; row < state.Height; row++)
        {
            int y = state.Height - 1 - row;
            char[] symbols = new char[state.Width];
            for (int x = 0; x < state.Width; x++)
            {
                symbols[x] = state.CellIdAt(x, y) switch
                {
                    0 => '.',
                    1 => 'A',
                    2 => 'a',
                    3 => 'B',
                    _ => throw new InvalidDataException("Unexpected biological ID in G10.")
                };
            }

            rows[row] = new string(symbols);
        }

        return rows;
    }

    private static void AssertEnergy(GoldenEnergy expected, HamiltonianBreakdown actual)
    {
        AssertClose(expected.Contact, actual.Contact);
        AssertClose(expected.Area, actual.Area);
        AssertClose(expected.Perimeter, actual.Perimeter);
        AssertClose(expected.Total, actual.Total);
    }

    private static void AssertNullableClose(double? expected, double? actual)
    {
        Assert.Equal(expected.HasValue, actual.HasValue);
        if (expected.HasValue)
        {
            AssertClose(expected.Value, actual!.Value);
        }
    }

    private static void AssertClose(double expected, double actual) =>
        Assert.True(Math.Abs(expected - actual) <= 1e-10 + 1e-12 * Math.Max(Math.Abs(expected), Math.Abs(actual)),
            $"Expected {expected:R}, actual {actual:R}.");

    private sealed record GoldenFixture(
        string RandomAlgorithm,
        string Seed,
        double FluctuationAmplitude,
        int Steps,
        string[] InitialRows,
        GoldenCell[] Cells,
        double[][] ContactEnergies,
        GoldenAttempt[] Attempts,
        string[] FinalRows,
        GoldenEnergy FinalHamiltonian);

    private sealed record GoldenCell(
        int CellId,
        int CellTypeId,
        double TargetArea,
        double AreaStiffness,
        double TargetPerimeter,
        double PerimeterStiffness);

    private sealed record GoldenPoint(int X, int Y);

    private sealed record GoldenEnergy(double Contact, double Area, double Perimeter, double Total);

    private sealed record GoldenAttempt(
        int Index,
        GoldenPoint Target,
        GoldenPoint Source,
        int OldCellId,
        int NewCellId,
        string Status,
        GoldenEnergy DeltaH,
        double? AcceptanceProbability,
        double? AcceptanceRandomValue,
        string[] RowsAfter);
}
