using System.Reflection;
using Rowles.Evolution.Algorithms;
using Rowles.Evolution.Archives;
using Rowles.Evolution.Evaluation;
using Rowles.Evolution.Metrics;
using Rowles.Evolution.Variation;

namespace Rowles.Evolution.Tests.Archives;

public sealed class GridMapElitesArchiveTests
{
    [Fact]
    public void OwnedArchiveIsExposedOnlyThroughReadOnlySurface()
    {
        GridMapElites algorithm = Create(702);
        PropertyInfo archiveProperty = typeof(GridMapElites).GetProperty(nameof(GridMapElites.Archive))!;

        Assert.Equal(typeof(IReadOnlyGridArchive), archiveProperty.PropertyType);
        Assert.False(typeof(GridArchive).IsInstanceOfType(algorithm.Archive));
        Assert.DoesNotContain(typeof(IReadOnlyGridArchive).GetMethods(), method => method.Name == nameof(GridArchive.TryInsert));
    }

    [Fact]
    public void ReadOnlyArchivePreservesLookupCoverageOrderingAndRestoredState()
    {
        GridMapElites algorithm = Create(703);
        IReadOnlyList<NumericCandidate> batch = algorithm.Ask(16);
        algorithm.Tell(batch.Select(candidate => CandidateEvaluation.Valid(
            candidate.CandidateId,
            1 - candidate.Values.Sum(value => value * value),
            [candidate.Values[0], candidate.Values[1]])).ToArray());

        IReadOnlyGridArchive archive = algorithm.Archive;
        int[] occupiedCells = archive.OccupiedElites.Select(elite => elite.CellIndex).ToArray();
        Assert.NotEmpty(occupiedCells);
        Assert.Equal(occupiedCells.Order(), occupiedCells);
        Assert.Equal(archive.Occupancy, occupiedCells.Length);
        Assert.Equal((double)archive.Occupancy / archive.Configuration.CellCount, archive.Coverage);
        Assert.NotNull(archive.GetAt(occupiedCells[0]));

        QdProgressMetrics expectedMetrics = algorithm.GetMetrics();
        GridMapElites restored = GridMapElites.Restore(algorithm.CreateCheckpointJson());
        QdProgressMetrics actualMetrics = restored.GetMetrics();
        Assert.Equal(expectedMetrics.Iteration, actualMetrics.Iteration);
        Assert.Equal(expectedMetrics.EvaluationsPerformed, actualMetrics.EvaluationsPerformed);
        Assert.Equal(expectedMetrics.ArchiveOccupancy, actualMetrics.ArchiveOccupancy);
        Assert.Equal(expectedMetrics.ArchiveCoverage, actualMetrics.ArchiveCoverage);
        Assert.Equal(expectedMetrics.QdScore, actualMetrics.QdScore);
        Assert.Equal(expectedMetrics.BestObjective, actualMetrics.BestObjective);
        Assert.Equal(expectedMetrics.ArchiveInsertions, actualMetrics.ArchiveInsertions);
        Assert.Equal(expectedMetrics.ArchiveReplacements, actualMetrics.ArchiveReplacements);
        Assert.Equal(expectedMetrics.InvalidEvaluations, actualMetrics.InvalidEvaluations);
        Assert.Equal(expectedMetrics.FailureCounts.OrderBy(pair => pair.Key),
            actualMetrics.FailureCounts.OrderBy(pair => pair.Key));
        Assert.Equal(archive.Occupancy, restored.Archive.Occupancy);
        Assert.Equal(archive.Coverage, restored.Archive.Coverage);
        Assert.Equal(archive.OccupiedElites.Select(Describe), restored.Archive.OccupiedElites.Select(Describe));
    }

    private static GridMapElites Create(ulong seed) => new(new GridMapElitesConfiguration(
        [new NumericBounds(-1, 1), new NumericBounds(-1, 1)],
        new GridArchiveConfiguration([-1, -1], [1, 1], [16, 16], ObjectiveDirection.Maximise, 0)), seed);

    private static string Describe(GridElite elite) => string.Join('|',
        elite.CellIndex,
        elite.CandidateId,
        elite.Objective.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        elite.Iteration,
        string.Join(',', elite.Solution.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))),
        string.Join(',', elite.Descriptors.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
}
