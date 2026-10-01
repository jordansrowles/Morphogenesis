using System.Security.Cryptography;
using System.Text;
using Rowles.Evolution.Algorithms;
using Rowles.Evolution.Archives;
using Rowles.Evolution.Evaluation;
using Rowles.Evolution.Metrics;
using Rowles.Evolution.Variation;

namespace Rowles.Evolution.Tests.Algorithms;

public sealed class GridMapElitesTests
{
    [Fact]
    public void TellCanonicalisesResultsAndRequiresCompleteUniqueBatch()
    {
        GridMapElites algorithm = Create(73);
        IReadOnlyList<NumericCandidate> batch = algorithm.Ask(4);
        Assert.Throws<InvalidOperationException>(() => algorithm.Ask(1));
        CandidateEvaluation[] reverse = batch.Reverse().Select(candidate => CandidateEvaluation.Valid(
            candidate.CandidateId, 1 - candidate.Values.Sum(value => value * value), candidate.Values)).ToArray();
        algorithm.Tell(reverse);
        GridMapElites inOrder = Create(73);
        IReadOnlyList<NumericCandidate> inOrderBatch = inOrder.Ask(4);
        inOrder.Tell(inOrderBatch.Select(candidate => CandidateEvaluation.Valid(
            candidate.CandidateId, 1 - candidate.Values.Sum(value => value * value), candidate.Values)).ToArray());
        Assert.Equal(inOrder.CreateCheckpointJson(), algorithm.CreateCheckpointJson());
        Assert.Equal(4, algorithm.EvaluationsPerformed);
        Assert.Equal(4, algorithm.Archive.Occupancy);

        IReadOnlyList<NumericCandidate> next = algorithm.Ask(2);
        CandidateEvaluation one = CandidateEvaluation.Valid(next[0].CandidateId, 1, [0, 0]);
        Assert.Throws<ArgumentException>(() => algorithm.Tell([one, one]));
        Assert.True(algorithm.HasOutstandingBatch);
    }

    [Fact]
    public void InvalidEvaluationsAreCountedButNeverBecomeElites()
    {
        GridMapElites algorithm = Create(99);
        IReadOnlyList<NumericCandidate> batch = algorithm.Ask(2);
        QdProgressMetrics metrics = algorithm.Tell(batch.Select(candidate => CandidateEvaluation.Invalid(
            candidate.CandidateId, "replicate-failed")).ToArray());
        Assert.Equal(2, metrics.InvalidEvaluations);
        Assert.Equal(2, metrics.FailureCounts["replicate-failed"]);
        Assert.Equal(0, metrics.ArchiveOccupancy);
        Assert.Null(metrics.BestObjective);
    }

    [Fact]
    public void SyntheticQuadraticCampaignHasKnownOptimumAndRepeatableArchive()
    {
        GridMapElites first = Create(20260929);
        RunSynthetic(first, 128, 64);
        GridMapElites second = Create(20260929);
        RunSynthetic(second, 128, 64);
        Assert.Equal(first.CreateCheckpointJson(), second.CreateCheckpointJson());
        Assert.Equal("1601F8229655AF8B1E2BA779C6C70014DD00E495480CE50D6AC14C3610C7470F",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(first.CreateCheckpointJson()))));
        Assert.Equal(1, Evaluate([0, 0]).Objective);
        Assert.Equal(new double[] { 0, 0 }, Evaluate([0, 0]).Descriptors);
        Assert.True(first.Archive.Occupancy > 0);
        Assert.Equal(8192, first.EvaluationsPerformed);
    }

    private static GridMapElites Create(ulong seed) => new(new GridMapElitesConfiguration(
        [new NumericBounds(-1, 1), new NumericBounds(-1, 1)],
        new GridArchiveConfiguration([-1, -1], [1, 1], [16, 16], ObjectiveDirection.Maximise, 0)), seed);

    private static CandidateEvaluation Evaluate(NumericCandidate candidate) => CandidateEvaluation.Valid(
        candidate.CandidateId, 1 - candidate.Values.Sum(value => value * value), candidate.Values);
    private static CandidateEvaluation Evaluate(IReadOnlyList<double> solution) =>
        CandidateEvaluation.Valid(0, 1 - solution.Sum(value => value * value), solution);

    private static void RunSynthetic(GridMapElites algorithm, int batches, int batchSize)
    {
        for (int i = 0; i < batches; i++)
        {
            IReadOnlyList<NumericCandidate> batch = algorithm.Ask(batchSize);
            algorithm.Tell(batch.Select(Evaluate).ToArray());
        }
    }
}
