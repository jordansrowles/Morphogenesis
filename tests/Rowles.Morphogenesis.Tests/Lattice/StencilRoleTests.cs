using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Tests.Fixtures;
using ReferenceCopyNeighbourhood = Rowles.Morphogenesis.Reference.Lattice.CopyNeighbourhood;
using ReferenceContactNeighbourhood = Rowles.Morphogenesis.Reference.Lattice.ContactCouplingNeighbourhood;
using ReferenceConnectivityAdjacency = Rowles.Morphogenesis.Reference.Lattice.ConnectivityAdjacency;
using ReferenceModelConventions = Rowles.Morphogenesis.Reference.Lattice.ModelConventions;
using ReferencePerimeterNeighbourhood = Rowles.Morphogenesis.Reference.Lattice.PerimeterNeighbourhood;

namespace Rowles.Morphogenesis.Tests.Lattice;

public sealed class StencilRoleTests
{
    [Fact]
    public void Contact_and_perimeter_stencils_are_configured_and_applied_independently()
    {
        string[] rows = [".....", ".....", "..A..", ".....", "....."];
        CellDefinition[] cells = [new(1, 1, 1, 0, 8, 0)];
        double[,] contacts = { { 0, 2 }, { 2, 0 } };
        LatticeConventions conventions = new(
            CopyNeighbourhood.VonNeumann,
            ContactCouplingNeighbourhood.VonNeumann,
            PerimeterNeighbourhood.Moore,
            ConnectivityAdjacency.VonNeumann);
        MorphogenesisState production = ProductionTestStateFactory.FromRows(
            rows,
            cells,
            contacts,
            new SimulationConfiguration(BoundaryMode.Periodic, conventions));

        int[,] referenceIds = new int[5, 5];
        referenceIds[2, 2] = 1;
        Rowles.Morphogenesis.Reference.Model.ReferenceState reference = new(
            referenceIds,
            [new Rowles.Morphogenesis.Reference.Model.CellDefinition(1, 1, 1, 0, 8, 0)],
            [new Rowles.Morphogenesis.Reference.Model.CellTypeDefinition(0, "Medium"), new(1, "A")],
            new Rowles.Morphogenesis.Reference.Model.ContactEnergyMatrix(contacts),
            new ReferenceModelConventions(
                ReferenceCopyNeighbourhood.VonNeumann,
                ReferenceContactNeighbourhood.VonNeumann,
                ReferencePerimeterNeighbourhood.Moore,
                ReferenceConnectivityAdjacency.VonNeumann));

        int target = 2 * 5 + 3;
        MoveEvaluation actual = EnergyDeltaCalculator.Evaluate(production, target, 1);
        Rowles.Morphogenesis.Reference.Energy.MoveDelta expected =
            Rowles.Morphogenesis.Reference.Energy.LocalMoveDelta.Evaluate(
                reference,
                new Rowles.Morphogenesis.Reference.Lattice.GridPoint(2, 2),
                new Rowles.Morphogenesis.Reference.Lattice.GridPoint(3, 2));

        Assert.Equal(CopyNeighbourhood.VonNeumann, production.Configuration.Conventions.CopyNeighbourhood);
        Assert.Equal(ContactCouplingNeighbourhood.VonNeumann, production.Configuration.Conventions.ContactCouplingNeighbourhood);
        Assert.Equal(PerimeterNeighbourhood.Moore, production.Configuration.Conventions.PerimeterNeighbourhood);
        Assert.Equal(ConnectivityAdjacency.VonNeumann, production.Configuration.Conventions.ConnectivityAdjacency);
        Assert.Equal(4, actual.Terms.Contact);
        Assert.Equal(6, actual.NewCellPerimeterDelta);
        AssertClose(expected.Terms.Contact, actual.Terms.Contact);
        AssertClose(expected.Terms.Perimeter, actual.Terms.Perimeter);
    }

    private static void AssertClose(double expected, double actual) =>
        Assert.True(Math.Abs(expected - actual) <= 1e-10 + 1e-12 * Math.Max(Math.Abs(expected), Math.Abs(actual)),
            $"Expected {expected:R}, actual {actual:R}.");
}
