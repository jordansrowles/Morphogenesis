using Rowles.Morphogenesis.Reference.Energy;
using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Tests.Fixtures;

namespace Rowles.Morphogenesis.Reference.Tests.Energy;

public sealed class ContactEnergyTests
{
    [Fact]
    public void Uniform_medium_and_uniform_biological_id_have_zero_contact_energy()
    {
        ReferenceState medium = TestStateFactory.FromRows([".....", ".....", ".....", ".....", "....."]);
        ReferenceState oneCell = TestStateFactory.FromRows(["AAAAA", "AAAAA", "AAAAA", "AAAAA", "AAAAA"]);

        Assert.Equal(0, ReferenceEnergy.ComputeContactEnergy(medium));
        Assert.Equal(0, ReferenceEnergy.ComputeContactEnergy(oneCell));
    }

    [Fact]
    public void One_cell_medium_interface_counts_eight_moore_pairs()
    {
        ReferenceState state = TestStateFactory.FromRows([".....", ".....", "..A..", ".....", "....."]);

        Assert.Equal(24, ReferenceEnergy.ComputeContactEnergy(state));
    }

    [Fact]
    public void Different_ids_of_the_same_type_still_form_one_contact_interface()
    {
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", ".Aa..", ".....", "....."],
            cellTypes: new Dictionary<int, int> { [1] = 1, [2] = 1 });

        Assert.Equal(44, ReferenceEnergy.ComputeContactEnergy(state));
        Assert.Equal(2, state.ContactEnergies[1, 1]);
    }

    [Fact]
    public void Periodic_seam_contact_pair_is_counted_once()
    {
        double[,] contacts =
        {
            { 0, 3, 3 },
            { 3, 2, 4 },
            { 3, 4, 1 }
        };
        ReferenceState state = TestStateFactory.FromRows(
            ["...", "B.A", "..."],
            cellTypes: new Dictionary<int, int> { [1] = 1, [3] = 2 },
            contactEnergies: contacts);

        Assert.Equal(46, ReferenceEnergy.ComputeContactEnergy(state));
        MoveDelta local = LocalMoveDelta.Evaluate(state, new GridPoint(0, 1), new GridPoint(2, 1));
        BruteForceMove full = BruteForceMoveReference.Evaluate(state, new GridPoint(0, 1), new GridPoint(2, 1));
        Assert.Equal(-4, local.Terms.Contact);
        Assert.Equal(full.Terms.Contact, local.Terms.Contact);
    }

    [Fact]
    public void Contact_pair_count_is_once_but_perimeter_counts_each_cell_side()
    {
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", ".Aa..", ".....", "....."],
            cellTypes: new Dictionary<int, int> { [1] = 1, [2] = 1 });

        Assert.Equal(44, ReferenceEnergy.ComputeContactEnergy(state));
        Assert.Equal(8, ReferenceEnergy.ComputePerimeter(state, 1));
        Assert.Equal(8, ReferenceEnergy.ComputePerimeter(state, 2));
    }

    [Fact]
    public void Anisotropy_probe_records_orientation_sensitivity_for_both_coupling_stencils()
    {
        ReferenceState cardinalCross = TestStateFactory.FromRows(
            [".......", ".......", "...A...", "..AAA..", "...A...", ".......", "......."]);
        ReferenceState diagonalCross = TestStateFactory.FromRows(
            [".......", ".......", "..A.A..", "...A...", "..A.A..", ".......", "......."]);
        double cardinalVonNeumann = ReferenceEnergy.ComputeContactEnergy(cardinalCross, ContactCouplingNeighbourhood.VonNeumann);
        double diagonalVonNeumann = ReferenceEnergy.ComputeContactEnergy(diagonalCross, ContactCouplingNeighbourhood.VonNeumann);
        double cardinalMoore = ReferenceEnergy.ComputeContactEnergy(cardinalCross, ContactCouplingNeighbourhood.Moore);
        double diagonalMoore = ReferenceEnergy.ComputeContactEnergy(diagonalCross, ContactCouplingNeighbourhood.Moore);
        int cardinalPerimeter = ReferenceEnergy.ComputePerimeter(cardinalCross, 1);
        int diagonalPerimeter = ReferenceEnergy.ComputePerimeter(diagonalCross, 1);
        string diagnostic = $"VN: cardinal={cardinalVonNeumann:R}, diagonal={diagonalVonNeumann:R}; " +
            $"Moore: cardinal={cardinalMoore:R}, diagonal={diagonalMoore:R}; " +
            $"Moore perimeters: cardinal={cardinalPerimeter}, diagonal={diagonalPerimeter}";

        Assert.Equal(36, cardinalVonNeumann);
        Assert.Equal(60, diagonalVonNeumann);
        Assert.Equal(72, cardinalMoore);
        Assert.Equal(96, diagonalMoore);
        Assert.Equal(24, cardinalPerimeter);
        Assert.Equal(32, diagonalPerimeter);
    }
}
