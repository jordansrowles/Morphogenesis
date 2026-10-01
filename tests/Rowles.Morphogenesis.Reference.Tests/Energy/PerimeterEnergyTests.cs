using Rowles.Morphogenesis.Reference.Energy;
using Rowles.Morphogenesis.Reference.Model;
using Rowles.Morphogenesis.Reference.Tests.Fixtures;

namespace Rowles.Morphogenesis.Reference.Tests.Energy;

public sealed class PerimeterEnergyTests
{
    [Fact]
    public void Single_site_has_moore_perimeter_eight()
    {
        ReferenceState state = TestStateFactory.FromRows([".....", ".....", "..A..", ".....", "....."]);

        Assert.Equal(8, ReferenceEnergy.ComputePerimeter(state, 1));
    }

    [Theory]
    [InlineData("horizontal", 14)]
    [InlineData("vertical", 14)]
    [InlineData("square", 20)]
    [InlineData("L", 18)]
    [InlineData("thin-line", 20)]
    public void G06_hand_calculated_shapes_have_expected_perimeter(string shape, int expected)
    {
        ReferenceState state = shape switch
        {
            "horizontal" => TestStateFactory.FromRows([".......", ".......", "..AA...", ".......", "......."]),
            "vertical" => TestStateFactory.FromRows([".......", "...A...", "...A...", ".......", "......."]),
            "square" => TestStateFactory.FromRows([".......", ".......", "..AA...", "..AA...", "......."]),
            "L" => TestStateFactory.FromRows([".......", ".......", "..AA...", "..A....", "......."]),
            "thin-line" => TestStateFactory.FromRows([".......", ".......", ".AAA...", ".......", "......."]),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        Assert.Equal(expected, ReferenceEnergy.ComputePerimeter(state, 1));
    }

    [Fact]
    public void Cell_cell_interface_contributes_to_both_perimeters()
    {
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", ".Aa..", ".....", "....."],
            cellTypes: new Dictionary<int, int> { [1] = 1, [2] = 1 });

        Assert.Equal(8, ReferenceEnergy.ComputePerimeter(state, 1));
        Assert.Equal(8, ReferenceEnergy.ComputePerimeter(state, 2));
    }

    [Fact]
    public void Shape_crossing_a_periodic_seam_has_domino_perimeter()
    {
        ReferenceState state = TestStateFactory.FromRows([".....", ".....", "A...A", ".....", "....."]);

        Assert.Equal(14, ReferenceEnergy.ComputePerimeter(state, 1));
    }

    [Fact]
    public void Perimeter_energy_matches_a_hand_calculated_single_cell_ledger()
    {
        CellDefinition cell = new(1, 1, TargetArea: 0, AreaStiffness: 2, TargetPerimeter: 6, PerimeterStiffness: 0.5);
        ReferenceState state = TestStateFactory.FromRows(
            [".....", ".....", "..A..", ".....", "....."],
            definitions: [cell]);

        HamiltonianBreakdown energy = ReferenceEnergy.ComputeHamiltonian(state);

        Assert.Equal(24, energy.Contact);
        Assert.Equal(2, energy.Area);
        Assert.Equal(2, energy.Perimeter);
        Assert.Equal(28, energy.Total);
    }
}
