using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Measurements;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Tests.Measurements;

public sealed class TissueMeasurementTests
{
    [Fact]
    public void Pure_A_counts_only_homotypic_cell_interfaces()
    {
        MorphogenesisState state = CreateState(2, 2, [1, 1, 2, 2], [(1, 1), (2, 1)]);

        TissueMeasurements result = Measure(state);

        Assert.Equal(0, result.HeterotypicInterfaceCount);
        Assert.Equal(2, result.TotalCellCellInterfaceCount);
        Assert.Equal(0, result.HeterotypicInterfaceFraction);
        Assert.Equal(2, result.HomotypicInterfacesByType[0].Count);
        Assert.Equal(0, result.HomotypicInterfacesByType[1].Count);
        Assert.Equal(2, result.TypeACellCount);
        Assert.Equal(0, result.TypeBCellCount);
        Assert.Equal(2, result.MinimumCellArea);
        Assert.Equal(2, result.MaximumCellArea);
        Assert.Equal(14, result.MinimumCellPerimeter);
        Assert.Equal(14, result.MaximumCellPerimeter);
    }

    [Fact]
    public void Pure_B_counts_homotypic_interfaces_under_type_B()
    {
        MorphogenesisState state = CreateState(2, 2, [1, 1, 2, 2], [(1, 2), (2, 2)]);

        TissueMeasurements result = Measure(state);

        Assert.Equal(0, result.HeterotypicInterfaceCount);
        Assert.Equal(2, result.TotalCellCellInterfaceCount);
        Assert.Equal(0, result.HomotypicInterfacesByType[0].Count);
        Assert.Equal(2, result.HomotypicInterfacesByType[1].Count);
        Assert.Equal(0, result.TypeACellCount);
        Assert.Equal(2, result.TypeBCellCount);
    }

    [Fact]
    public void Separated_A_and_B_halves_have_two_heterotypic_pairs()
    {
        MorphogenesisState state = CreateState(2, 2, [1, 3, 1, 3], [(1, 1), (3, 2)]);

        TissueMeasurements result = Measure(state);

        Assert.Equal(2, result.HeterotypicInterfaceCount);
        Assert.Equal(2, result.TotalCellCellInterfaceCount);
        Assert.Equal(1, result.HeterotypicInterfaceFraction);
        Assert.Equal(1, result.DomainsByType[0].DomainCount);
        Assert.Equal(1, result.DomainsByType[1].DomainCount);
    }

    [Fact]
    public void Alternating_checkerboard_counts_each_unordered_pair_once()
    {
        MorphogenesisState state = CreateState(2, 2, [1, 2, 3, 4], [(1, 1), (2, 2), (3, 2), (4, 1)]);

        TissueMeasurements result = Measure(state);

        Assert.Equal(4, result.HeterotypicInterfaceCount);
        Assert.Equal(4, result.TotalCellCellInterfaceCount);
        Assert.Equal(1, result.HeterotypicInterfaceFraction);
    }

    [Fact]
    public void Two_distinct_same_type_cells_share_one_homotypic_interface()
    {
        MorphogenesisState state = CreateState(2, 1, [1, 2], [(1, 1), (2, 1)]);

        TissueMeasurements result = Measure(state);

        Assert.Equal(0, result.HeterotypicInterfaceCount);
        Assert.Equal(1, result.TotalCellCellInterfaceCount);
        Assert.Equal(1, result.HomotypicInterfacesByType[0].Count);
    }

    [Fact]
    public void Medium_contacts_do_not_enter_cell_cell_interface_counts()
    {
        MorphogenesisState state = CreateState(3, 1, [1, 2, 0], [(1, 1), (2, 2)]);

        TissueMeasurements result = Measure(state);

        Assert.Equal(1, result.HeterotypicInterfaceCount);
        Assert.Equal(1, result.TotalCellCellInterfaceCount);
        Assert.Equal(1, result.HeterotypicInterfaceFraction);
    }

    [Fact]
    public void Zero_cell_cell_interfaces_have_a_finite_zero_fraction()
    {
        MorphogenesisState state = CreateState(3, 1, [1, 0, 2], [(1, 1), (2, 2)]);

        TissueMeasurements result = Measure(state);

        Assert.Equal(0, result.HeterotypicInterfaceCount);
        Assert.Equal(0, result.TotalCellCellInterfaceCount);
        Assert.Equal(0, result.HeterotypicInterfaceFraction);
        Assert.True(double.IsFinite(result.HeterotypicInterfaceFraction));
    }

    [Fact]
    public void Periodic_seam_contact_is_counted_once()
    {
        int[] ids = new int[9];
        ids[3] = 1;
        ids[5] = 2;
        MorphogenesisState state = CreateState(3, 3, ids, [(1, 1), (2, 2)], BoundaryMode.Periodic);

        TissueMeasurements result = Measure(state);

        Assert.Equal(1, result.HeterotypicInterfaceCount);
        Assert.Equal(1, result.TotalCellCellInterfaceCount);
    }

    private static TissueMeasurements Measure(MorphogenesisState state) =>
        TissueMeasurementCalculator.Measure(state, 1, 2, ContactCouplingNeighbourhood.VonNeumann);

    private static MorphogenesisState CreateState(
        int width,
        int height,
        int[] ids,
        (int CellId, int TypeId)[] cells,
        BoundaryMode boundaryMode = BoundaryMode.Wall)
    {
        CellDefinition[] definitions = cells.Select(cell => new CellDefinition(cell.CellId, cell.TypeId, 0, 0, 0, 0)).ToArray();
        return new MorphogenesisState(
            width,
            height,
            ids,
            definitions,
            new ContactEnergyMatrix(
                new double[,]
                {
                    { 0, 0, 0 },
                    { 0, 0, 0 },
                    { 0, 0, 0 }
                }),
            new SimulationConfiguration(boundaryMode, LatticeConventions.Canonical));
    }
}
