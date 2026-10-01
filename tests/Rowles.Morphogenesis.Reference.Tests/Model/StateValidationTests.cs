using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Model;
using Rowles.Morphogenesis.Reference.Tests.Fixtures;

namespace Rowles.Morphogenesis.Reference.Tests.Model;

public sealed class StateValidationTests
{
    [Fact]
    public void Medium_is_id_zero_and_maps_to_type_zero()
    {
        ReferenceState state = TestStateFactory.FromRows([".....", ".....", "..A..", ".....", "....."]);

        Assert.Equal(0, state.CellIdAt(0, 0));
        Assert.Equal(0, state.CellTypeIdForCell(0));
        Assert.Equal("Medium", state.CellTypes[0].Name);
    }

    [Fact]
    public void Negative_cell_ids_are_rejected()
    {
        int[,] ids = new int[5, 5];
        ids[2, 2] = -1;

        Assert.Throws<ArgumentOutOfRangeException>(() => new ReferenceState(
            ids,
            [],
            [new CellTypeDefinition(0, "Medium")],
            new ContactEnergyMatrix(new double[,] { { 0 } })));
    }

    [Fact]
    public void Biological_ids_require_metadata()
    {
        int[,] ids = new int[5, 5];
        ids[2, 2] = 9;

        Assert.Throws<ArgumentException>(() => new ReferenceState(
            ids,
            [],
            [new CellTypeDefinition(0, "Medium"), new CellTypeDefinition(1, "A")],
            new ContactEnergyMatrix(new double[,] { { 0, 1 }, { 1, 0 } })));
    }

    [Fact]
    public void Invalid_cell_type_indexes_are_rejected()
    {
        int[,] ids = new int[5, 5];

        Assert.Throws<ArgumentOutOfRangeException>(() => new ReferenceState(
            ids,
            [new CellDefinition(1, 1, 1, 1, 8, 1)],
            [new CellTypeDefinition(0, "Medium")],
            new ContactEnergyMatrix(new double[,] { { 0 } })));
    }

    [Fact]
    public void Contact_matrix_must_be_square_symmetric_and_finite()
    {
        Assert.Throws<ArgumentException>(() => new ContactEnergyMatrix(new double[2, 3]));
        Assert.Throws<ArgumentException>(() => new ContactEnergyMatrix(new double[,] { { 0, 1 }, { 2, 0 } }));
        Assert.Throws<ArgumentException>(() => new ContactEnergyMatrix(new double[,] { { 0, double.NaN }, { double.NaN, 0 } }));
    }

    [Fact]
    public void Invalid_hamiltonian_parameters_are_rejected()
    {
        int[,] ids = new int[5, 5];
        CellTypeDefinition[] types = [new(0, "Medium"), new(1, "A")];
        ContactEnergyMatrix matrix = new(new double[,] { { 0, 1 }, { 1, 0 } });

        Assert.Throws<ArgumentOutOfRangeException>(() => new ReferenceState(
            ids,
            [new CellDefinition(1, 1, 1, -1, 8, 1)],
            types,
            matrix));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReferenceState(
            ids,
            [new CellDefinition(1, 1, double.PositiveInfinity, 1, 8, 1)],
            types,
            matrix));
    }

    [Fact]
    public void Construction_copies_the_supplied_lattice_and_contact_matrix()
    {
        int[,] ids = new int[5, 5];
        ids[2, 2] = 1;
        double[,] contacts = { { 0, 3 }, { 3, 0 } };
        ReferenceState state = new(
            ids,
            [new CellDefinition(1, 1, 1, 0, 8, 0)],
            [new CellTypeDefinition(0, "Medium"), new CellTypeDefinition(1, "A")],
            new ContactEnergyMatrix(contacts));
        ids[2, 2] = 0;
        contacts[0, 1] = 99;

        Assert.Equal(1, state.CellIdAt(2, 2));
        Assert.Equal(3, state.ContactEnergies[0, 1]);
    }
}
