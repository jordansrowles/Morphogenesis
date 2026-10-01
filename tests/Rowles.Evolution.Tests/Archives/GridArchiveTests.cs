using Rowles.Evolution.Archives;

namespace Rowles.Evolution.Tests.Archives;

public sealed class GridArchiveTests
{
    [Fact]
    public void ClosedBoundsMapToFirstAndLastBinsAndRejectOutsideValues()
    {
        GridArchiveConfiguration config = new([0, -1], [1, 1], [4, 2], ObjectiveDirection.Maximise);
        Assert.Equal(0, config.GetCellIndex([0, -1]));
        Assert.Equal(7, config.GetCellIndex([1, 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => config.GetCellIndex([-double.Epsilon, 0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => config.GetCellIndex([0, double.NaN]));
    }

    [Fact]
    public void ReplacementRequiresStrictImprovement()
    {
        GridArchive archive = new(new GridArchiveConfiguration([0], [1], [2], ObjectiveDirection.Maximise));
        Assert.True(archive.TryInsert(0, [0.2], 2, [0.2], 0, out bool firstReplaced));
        Assert.False(firstReplaced);
        Assert.False(archive.TryInsert(1, [0.3], 2, [0.3], 0, out _));
        Assert.Equal(0, archive.GetAt(0)!.CandidateId);
        Assert.True(archive.TryInsert(2, [0.3], 3, [0.3], 0, out bool replaced));
        Assert.True(replaced);
        Assert.Equal(1, archive.Occupancy);
    }

    [Theory]
    [InlineData(ObjectiveDirection.Maximise, 2, 3, 1)]
    [InlineData(ObjectiveDirection.Minimise, 2, 1, 3)]
    public void BothObjectiveDirectionsReplaceOnlyOnStrictImprovement(
        ObjectiveDirection direction, double incumbent, double improvement, double deterioration)
    {
        GridArchive archive = new(new GridArchiveConfiguration([0], [1], [2], direction));
        Assert.True(archive.TryInsert(0, [0.2], incumbent, [0.2], 0, out bool initialReplacement));
        Assert.False(initialReplacement);

        Assert.False(archive.TryInsert(1, [0.3], incumbent, [0.3], 0, out bool tiedReplacement));
        Assert.False(tiedReplacement);
        Assert.False(archive.TryInsert(2, [0.3], deterioration, [0.3], 0, out bool worseReplacement));
        Assert.False(worseReplacement);
        Assert.True(archive.TryInsert(3, [0.3], improvement, [0.3], 0, out bool replacement));
        Assert.True(replacement);
        Assert.Equal(3, archive.GetAt(0)!.CandidateId);
    }

    [Fact]
    public void ProductIsGuardedBeforeAllocation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridArchiveConfiguration([0, 0, 0], [1, 1, 1], [1000, 1000, 100], ObjectiveDirection.Maximise));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridArchiveConfiguration([0, 0], [1, 1], [int.MaxValue, int.MaxValue], ObjectiveDirection.Maximise, maximumCells: int.MaxValue));
    }

}
