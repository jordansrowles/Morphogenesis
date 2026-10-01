using System.Text.Json;
using System.Text.Json.Nodes;
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

    [Fact]
    public void CheckpointWithCachedGaussianResumesTheSameNextCandidates()
    {
        GridMapElites uninterrupted = Create(8128);
        IReadOnlyList<NumericCandidate> initial = uninterrupted.Ask(2);
        uninterrupted.Tell(initial.Select(Evaluate).ToArray());
        IReadOnlyList<NumericCandidate> oddGaussianBatch = uninterrupted.Ask(1);
        uninterrupted.Tell(oddGaussianBatch.Select(Evaluate).ToArray());

        JsonObject state = JsonNode.Parse(uninterrupted.CreateCheckpointJson())!.AsObject();
        Assert.True(state["RandomState"]!["HasGaussian"]!.GetValue<bool>());

        GridMapElites resumed = GridMapElites.Restore(state.ToJsonString());
        IReadOnlyList<NumericCandidate> expected = uninterrupted.Ask(12);
        IReadOnlyList<NumericCandidate> actual = resumed.Ask(12);
        Assert.Equal(expected.Select(candidate => candidate.CandidateId), actual.Select(candidate => candidate.CandidateId));
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Values.Count, actual[i].Values.Count);
            for (int dimension = 0; dimension < expected[i].Values.Count; dimension++)
            {
                Assert.Equal(BitConverter.DoubleToInt64Bits(expected[i].Values[dimension]),
                    BitConverter.DoubleToInt64Bits(actual[i].Values[dimension]));
            }
        }
    }

    [Theory]
    [MemberData(nameof(CorruptCheckpointCases))]
    public void RestoreRejectsMalformedCheckpoint(string name, Action<JsonObject> corrupt)
    {
        JsonObject checkpoint = JsonNode.Parse(CreatePopulatedCheckpoint())!.AsObject();
        corrupt(checkpoint);

        Exception? exception = Record.Exception(() => GridMapElites.Restore(checkpoint.ToJsonString()));
        Assert.NotNull(exception);
        Assert.True(exception is ArgumentException or JsonException or NotSupportedException,
            $"Unexpected validation exception for {name}: {exception.GetType().FullName}");
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }

    public static IEnumerable<object[]> CorruptCheckpointCases()
    {
        yield return ["wrong solution length", (Action<JsonObject>)(checkpoint => Elite(checkpoint, 0)["Solution"] = new JsonArray(0.0))];
        yield return ["NaN solution", (Action<JsonObject>)(checkpoint => Elite(checkpoint, 0)["Solution"]!.AsArray()[0] = "NaN")];
        yield return ["positive infinity solution", (Action<JsonObject>)(checkpoint => Elite(checkpoint, 0)["Solution"]!.AsArray()[0] = "Infinity")];
        yield return ["solution below lower bound", (Action<JsonObject>)(checkpoint => Elite(checkpoint, 0)["Solution"]!.AsArray()[0] = -1.01)];
        yield return ["solution above upper bound", (Action<JsonObject>)(checkpoint => Elite(checkpoint, 0)["Solution"]!.AsArray()[0] = 1.01)];
        yield return ["wrong descriptor length", (Action<JsonObject>)(checkpoint => Elite(checkpoint, 0)["Descriptors"] = new JsonArray(0.0))];
        yield return ["NaN descriptor", (Action<JsonObject>)(checkpoint => Elite(checkpoint, 0)["Descriptors"]!.AsArray()[0] = "NaN")];
        yield return ["out of range descriptor", (Action<JsonObject>)(checkpoint => Elite(checkpoint, 0)["Descriptors"]!.AsArray()[0] = 1.01)];
        yield return ["cell index descriptor mismatch", (Action<JsonObject>)(checkpoint => Elite(checkpoint, 0)["CellIndex"] = Elite(checkpoint, 0)["CellIndex"]!.GetValue<int>() + 5)];
        yield return ["duplicate cell index", (Action<JsonObject>)(checkpoint => Elite(checkpoint, 1)["CellIndex"] = Elite(checkpoint, 0)["CellIndex"]!.GetValue<int>())];
        yield return ["duplicate candidate ID", (Action<JsonObject>)(checkpoint => Elite(checkpoint, 1)["CandidateId"] = Elite(checkpoint, 0)["CandidateId"]!.GetValue<long>())];
        yield return ["candidate ID is not below next ID", (Action<JsonObject>)(checkpoint => Elite(checkpoint, 0)["CandidateId"] = checkpoint["NextCandidateId"]!.GetValue<long>())];
        yield return ["elite iteration is not below checkpoint iteration", (Action<JsonObject>)(checkpoint => Elite(checkpoint, 0)["Iteration"] = checkpoint["Iteration"]!.GetValue<long>())];
        yield return ["non-finite objective", (Action<JsonObject>)(checkpoint => Elite(checkpoint, 0)["Objective"] = "NaN")];
        yield return ["counter inconsistency", (Action<JsonObject>)(checkpoint => checkpoint["Evaluations"] = 3)];
        yield return ["failure-count total mismatch", (Action<JsonObject>)(checkpoint => checkpoint["InvalidEvaluations"] = 1)];
        yield return ["failure reasons are not ordinally sorted", (Action<JsonObject>)(checkpoint =>
        {
            JsonArray failures = checkpoint["FailureCounts"]!.AsArray();
            JsonNode first = failures[0]!.DeepClone();
            JsonNode second = failures[1]!.DeepClone();
            failures.Clear();
            failures.Add(second);
            failures.Add(first);
        })];
        yield return ["duplicate failure reason", (Action<JsonObject>)(checkpoint =>
            checkpoint["FailureCounts"]!.AsArray()[1]!["Reason"] =
                checkpoint["FailureCounts"]!.AsArray()[0]!["Reason"]!.GetValue<string>())];
        yield return ["empty failure reason", (Action<JsonObject>)(checkpoint =>
            checkpoint["FailureCounts"]!.AsArray()[0]!["Reason"] = "")];
        yield return ["non-positive failure count", (Action<JsonObject>)(checkpoint =>
            checkpoint["FailureCounts"]!.AsArray()[0]!["Count"] = 0)];
        yield return ["all-zero random state", (Action<JsonObject>)(checkpoint =>
        {
            checkpoint["RandomState"]!["S0"] = 0;
            checkpoint["RandomState"]!["S1"] = 0;
            checkpoint["RandomState"]!["S2"] = 0;
            checkpoint["RandomState"]!["S3"] = 0;
        })];
        yield return ["non-finite unused Gaussian state", (Action<JsonObject>)(checkpoint =>
        {
            checkpoint["RandomState"]!["HasGaussian"] = false;
            checkpoint["RandomState"]!["Gaussian"] = "Infinity";
        })];
        yield return ["unsupported schema", (Action<JsonObject>)(checkpoint => checkpoint["SchemaVersion"] = 99)];
    }

    private static GridMapElites Create(ulong seed) => new(new GridMapElitesConfiguration(
        [new NumericBounds(-1, 1), new NumericBounds(-1, 1)],
        new GridArchiveConfiguration([-1, -1], [1, 1], [16, 16], ObjectiveDirection.Maximise, 0)), seed);

    private static CandidateEvaluation Evaluate(NumericCandidate candidate) => CandidateEvaluation.Valid(
        candidate.CandidateId, 1 - candidate.Values.Sum(value => value * value), candidate.Values);

    private static string CreatePopulatedCheckpoint()
    {
        GridMapElites algorithm = Create(981);
        IReadOnlyList<NumericCandidate> batch = algorithm.Ask(4);
        CandidateEvaluation[] results =
        [
            CandidateEvaluation.Valid(batch[0].CandidateId, 1, [0.2, 0.2]),
            CandidateEvaluation.Valid(batch[1].CandidateId, 2, [0.8, 0.8]),
            CandidateEvaluation.Invalid(batch[2].CandidateId, "z-failure"),
            CandidateEvaluation.Invalid(batch[3].CandidateId, "a-failure")
        ];
        algorithm.Tell(results);
        return algorithm.CreateCheckpointJson();
    }

    private static JsonObject Elite(JsonObject checkpoint, int index) =>
        checkpoint["Elites"]!.AsArray()[index]!.AsObject();
}
