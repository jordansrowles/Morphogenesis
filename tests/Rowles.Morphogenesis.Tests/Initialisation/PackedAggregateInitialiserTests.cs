using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Initialisation;
using Rowles.Morphogenesis.Tests.Experiments;

namespace Rowles.Morphogenesis.Tests.Initialisation;

public sealed class PackedAggregateInitialiserTests
{
    [Fact]
    public void Same_seed_and_specification_produce_identical_packed_tissues()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create() with
        {
            Initialiser = ExperimentManifestFactory.Create().Initialiser with { CellCount = 12 }
        };

        PackedAggregateInitialisation first = PackedAggregateInitialiser.Create(manifest, 37);
        PackedAggregateInitialisation second = PackedAggregateInitialiser.Create(manifest, 37);

        Assert.Equal(first.State.GetCellIdsCopy(), second.State.GetCellIdsCopy());
        Assert.Equal(ReadTypes(first.State, 12), ReadTypes(second.State, 12));
    }

    [Fact]
    public void Different_seeds_change_type_assignment_while_preserving_the_requested_proportion()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create() with
        {
            Initialiser = ExperimentManifestFactory.Create().Initialiser with { CellCount = 12 }
        };

        PackedAggregateInitialisation first = PackedAggregateInitialiser.Create(manifest, 37);
        PackedAggregateInitialisation second = PackedAggregateInitialiser.Create(manifest, 91);

        Assert.NotEqual(ReadTypes(first.State, 12), ReadTypes(second.State, 12));
        Assert.Equal(6, first.TypeACellCount);
        Assert.Equal(6, first.TypeBCellCount);
        Assert.Equal(first.TypeACellCount, second.TypeACellCount);
    }

    [Fact]
    public void Every_requested_cell_is_unique_connected_and_present_in_the_validated_state()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create() with
        {
            Initialiser = ExperimentManifestFactory.Create().Initialiser with { CellCount = 12 }
        };

        PackedAggregateInitialisation initialisation = PackedAggregateInitialiser.Create(manifest, 123);
        int[] ids = initialisation.State.GetCellIdsCopy();
        initialisation.State.ValidateInvariants();

        Assert.Equal(12, Enumerable.Range(1, 12).Distinct().Count());
        Assert.Equal(12, ids.Where(id => id > 0).Distinct().Count());
        Assert.All(Enumerable.Range(1, 12), cellId => Assert.True(ids.Contains(cellId)));
        Assert.All(Enumerable.Range(1, 12), cellId => Assert.Equal(4, initialisation.State.GetCellState(cellId).Area));
        Assert.Equal(4, initialisation.ActualCellArea);
        Assert.True(ids.Contains(0));
    }

    [Fact]
    public void Target_area_rounding_is_reported_instead_of_hidden()
    {
        ExperimentManifest original = ExperimentManifestFactory.Create();
        ExperimentManifest manifest = original with
        {
            Initialiser = original.Initialiser with { ApproximateTargetCellArea = 10 },
            Mechanics = original.Mechanics with { TargetArea = 10 }
        };

        PackedAggregateInitialisation initialisation = PackedAggregateInitialiser.Create(manifest, 3);

        Assert.Equal(12, initialisation.ActualCellArea);
        Assert.Equal(12, initialisation.State.GetCellState(1).Area);
    }

    [Fact]
    public void Single_cell_population_is_supported()
    {
        ExperimentManifest original = ExperimentManifestFactory.Create();
        ExperimentManifest manifest = original with
        {
            Initialiser = original.Initialiser with { CellCount = 1, TypeAProportion = 0 }
        };

        PackedAggregateInitialisation initialisation = PackedAggregateInitialiser.Create(manifest, 9);

        Assert.Equal(0, initialisation.TypeACellCount);
        Assert.Equal(1, initialisation.TypeBCellCount);
        Assert.Equal(2, initialisation.State.GetCellState(1).CellTypeId);
        initialisation.State.ValidateInvariants();
    }

    [Fact]
    public void Impossible_configurations_fail_clearly()
    {
        ExperimentManifest original = ExperimentManifestFactory.Create();
        ExperimentManifest manifest = original with
        {
            GridWidth = 3,
            GridHeight = 3,
            Initialiser = original.Initialiser with { CellCount = 8 }
        };

        Assert.Throws<ArgumentException>(() => PackedAggregateInitialiser.Create(manifest, 1));
    }

    private static int[] ReadTypes(Rowles.Morphogenesis.Model.MorphogenesisState state, int cellCount) =>
        Enumerable.Range(1, cellCount).Select(cellId => state.GetCellState(cellId).CellTypeId).ToArray();
}
