using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Tests.Fixtures;
using ReferenceBruteForceMove = Rowles.Morphogenesis.Reference.Energy.BruteForceMoveReference;
using ReferenceEnergy = Rowles.Morphogenesis.Reference.Energy.ReferenceEnergy;
using ReferenceGridPoint = Rowles.Morphogenesis.Reference.Lattice.GridPoint;

namespace Rowles.Morphogenesis.Tests.Energy;

public sealed class EnergyDeltaTests
{
    private static readonly double[,] Contacts =
    {
        { 0, 2, 3 },
        { 2, 1.5, 2.25 },
        { 3, 2.25, 1 }
    };

    private static readonly CellDefinition[] Cells =
    [
        new(1, 1, 2, 1.25, 10, 0.08),
        new(3, 2, 2, 0.85, 8, 0.06)
    ];

    [Fact]
    public void Every_periodic_copy_delta_matches_full_recomputation_and_reference_implementation()
    {
        string[] rows =
        [
            ".....",
            "..BB.",
            ".AAB.",
            ".....",
            "....."
        ];
        MorphogenesisState production = ProductionTestStateFactory.FromRows(rows, Cells, Contacts);
        Rowles.Morphogenesis.Reference.Model.ReferenceState reference =
            ProductionTestStateFactory.ToReference(rows, Cells, Contacts);
        AssertTermsEqual(ReferenceEnergy.ComputeHamiltonian(reference), production.RecomputeHamiltonian());

        int[,] offsets = { { 0, -1 }, { 1, 0 }, { 0, 1 }, { -1, 0 } };
        for (int target = 0; target < production.SiteCount; target++)
        {
            int x = target % production.Width;
            int y = target / production.Width;
            for (int direction = 0; direction < 4; direction++)
            {
                int sourceX = (x + offsets[direction, 0] + production.Width) % production.Width;
                int sourceY = (y + offsets[direction, 1] + production.Height) % production.Height;
                ReferenceGridPoint targetPoint = new(x, y);
                ReferenceGridPoint sourcePoint = new(sourceX, sourceY);
                Rowles.Morphogenesis.Reference.Energy.BruteForceMove expected =
                    ReferenceBruteForceMove.Evaluate(reference, sourcePoint, targetPoint);
                MoveEvaluation actual = EnergyDeltaCalculator.Evaluate(
                    production,
                    target,
                    production.CellIdAt(sourceY * production.Width + sourceX));

                AssertTermsEqual(expected.Terms, actual.Terms);
                AssertTermsEqual(
                    Rowles.Morphogenesis.Reference.Energy.LocalMoveDelta.Evaluate(reference, sourcePoint, targetPoint).Terms,
                    actual.Terms);
            }
        }
    }

    [Fact]
    public void Incremental_area_and_perimeter_commits_match_exact_recomputation()
    {
        string[] rows = ["..........", "..........", "...AA.....", "...AA.....", "..........", ".........."];
        CellDefinition[] cells = [new(1, 1, 4, 1, 12, 0.1)];
        double[,] contacts = { { 0, 0 }, { 0, 0 } };
        MorphogenesisState state = ProductionTestStateFactory.FromRows(rows, cells, contacts);
        Rowles.Morphogenesis.Dynamics.SerialSimulation simulation = new(
            state,
            new Rowles.Morphogenesis.Random.Xoshiro256StarStar(0x19A7UL),
            8);

        for (int mcs = 0; mcs < 40; mcs++)
        {
            McsSummary summary = simulation.RunMcs();
            Assert.Equal(state.SiteCount, summary.Attempts);
            state.ValidateInvariants();
            CellState cell = state.GetCellState(1);
            Assert.InRange(cell.Area, 1, state.SiteCount);
            Assert.InRange(cell.Perimeter, 0, 8 * cell.Area);
        }
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

    private static void AssertTermsEqual(HamiltonianBreakdown expected, HamiltonianBreakdown actual)
    {
        AssertClose(expected.Contact, actual.Contact);
        AssertClose(expected.Area, actual.Area);
        AssertClose(expected.Perimeter, actual.Perimeter);
        AssertClose(expected.Total, actual.Total);
    }

    private static void AssertClose(double expected, double actual) =>
        Assert.True(Math.Abs(expected - actual) <= 1e-10 + 1e-12 * Math.Max(Math.Abs(expected), Math.Abs(actual)),
            $"Expected {expected:R}, actual {actual:R}.");
}
