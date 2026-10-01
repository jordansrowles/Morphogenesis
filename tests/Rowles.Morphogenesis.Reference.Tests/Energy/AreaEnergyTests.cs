using Rowles.Morphogenesis.Reference.Energy;
using Rowles.Morphogenesis.Reference.Model;
using Rowles.Morphogenesis.Reference.Tests.Fixtures;

namespace Rowles.Morphogenesis.Reference.Tests.Energy;

public sealed class AreaEnergyTests
{
    [Fact]
    public void Empty_biological_cell_has_zero_area()
    {
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", ".....", ".....", "....."],
            definitions: [new CellDefinition(7, 1, 2, 1, 8, 0.25)]);

        Assert.Equal(0, ReferenceEnergy.ComputeArea(state, 7));
    }

    [Fact]
    public void Area_counts_each_cell_id_independently_even_for_cells_of_one_type()
    {
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", ".AAa.", ".A.a.", "....."],
            cellTypes: new Dictionary<int, int> { [1] = 1, [2] = 1 });

        Assert.Equal(3, ReferenceEnergy.ComputeArea(state, 1));
        Assert.Equal(2, ReferenceEnergy.ComputeArea(state, 2));
        Assert.Equal(20, ReferenceEnergy.ComputeArea(state, 0));
        Assert.Equal(1, state.CellTypeIdForCell(1));
        Assert.Equal(1, state.CellTypeIdForCell(2));
    }

    [Fact]
    public void Periodic_position_does_not_change_area()
    {
        ReferenceState centre = TestStateFactory.FromRows([".....", ".....", "..AA.", ".....", "....."]);
        ReferenceState seam = TestStateFactory.FromRows([".....", ".....", "A...A", ".....", "....."]);

        Assert.Equal(ReferenceEnergy.ComputeArea(centre, 1), ReferenceEnergy.ComputeArea(seam, 1));
    }

    [Fact]
    public void Area_energy_is_zero_at_target_and_one_for_equal_unit_errors()
    {
        CellDefinition cell = new(1, 1, TargetArea: 2, AreaStiffness: 1, TargetPerimeter: 8, PerimeterStiffness: 0);
        ReferenceState atTarget = TestStateFactory.FromRows([".....", ".....", ".AA..", ".....", "....."], definitions: [cell]);
        ReferenceState oneBelow = TestStateFactory.FromRows([".....", ".....", "..A..", ".....", "....."], definitions: [cell]);
        ReferenceState oneAbove = TestStateFactory.FromRows([".....", ".....", ".AAA.", ".....", "....."], definitions: [cell]);

        Assert.Equal(0, ReferenceEnergy.ComputeHamiltonian(atTarget).Area);
        Assert.Equal(1, ReferenceEnergy.ComputeHamiltonian(oneBelow).Area);
        Assert.Equal(1, ReferenceEnergy.ComputeHamiltonian(oneAbove).Area);
    }

    [Fact]
    public void Multiple_biological_cells_contribute_area_energy()
    {
        CellDefinition first = new(1, 1, TargetArea: 2, AreaStiffness: 2, TargetPerimeter: 0, PerimeterStiffness: 0);
        CellDefinition second = new(2, 1, TargetArea: 1, AreaStiffness: 0.5, TargetPerimeter: 0, PerimeterStiffness: 0);
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", ".Aaa.", "...a.", "....."],
            cellTypes: new Dictionary<int, int> { [1] = 1, [2] = 1 },
            definitions: [first, second]);

        Assert.Equal(4, ReferenceEnergy.ComputeHamiltonian(state).Area);
    }

    [Fact]
    public void Medium_has_no_area_or_perimeter_energy()
    {
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", ".....", ".....", "....."]);

        HamiltonianBreakdown energy = ReferenceEnergy.ComputeHamiltonian(state);

        Assert.Equal(0, energy.Contact);
        Assert.Equal(0, energy.Area);
        Assert.Equal(0, energy.Perimeter);
        Assert.Equal(state.SiteCount, ReferenceEnergy.ComputeArea(state, 0));
    }
}
