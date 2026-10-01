using Rowles.Evolution.Algorithms;
using Rowles.Evolution.Archives;
using Rowles.Evolution.Evaluation;
using Rowles.Evolution.Variation;

namespace Rowles.Evolution.Tests.Checkpointing;

public sealed class GridMapElitesCheckpointTests
{
    [Fact]
    public void CheckpointResumeEmitsTheSameNextBatch()
    {
        GridMapElites uninterrupted = Create(8128);
        IReadOnlyList<NumericCandidate> initial = uninterrupted.Ask(16);
        uninterrupted.Tell(initial.Select(Evaluate).ToArray());
        string checkpoint = uninterrupted.CreateCheckpointJson();
        GridMapElites resumed = GridMapElites.Restore(checkpoint);
        IReadOnlyList<NumericCandidate> first = uninterrupted.Ask(16);
        IReadOnlyList<NumericCandidate> second = resumed.Ask(16);
        Assert.Equal(first.Select(c => c.CandidateId), second.Select(c => c.CandidateId));
        Assert.Equal(first.Select(c => c.Values.ToArray()), second.Select(c => c.Values.ToArray()));
        Assert.Throws<InvalidOperationException>(() => uninterrupted.CreateCheckpointJson());
        Assert.Throws<NotSupportedException>(() => GridMapElites.Restore(checkpoint.Replace(
            "\"SchemaVersion\":1", "\"SchemaVersion\":99", StringComparison.Ordinal)));
    }

    private static GridMapElites Create(ulong seed) => new(new GridMapElitesConfiguration(
        [new NumericBounds(-1, 1), new NumericBounds(-1, 1)],
        new GridArchiveConfiguration([-1, -1], [1, 1], [16, 16], ObjectiveDirection.Maximise, 0)), seed);

    private static CandidateEvaluation Evaluate(NumericCandidate candidate) => CandidateEvaluation.Valid(
        candidate.CandidateId, 1 - candidate.Values.Sum(value => value * value), candidate.Values);
}
