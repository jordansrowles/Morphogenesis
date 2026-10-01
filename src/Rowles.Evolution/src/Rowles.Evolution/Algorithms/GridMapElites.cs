using Rowles.Evolution.Archives;
using Rowles.Evolution.Evaluation;
using Rowles.Evolution.Metrics;
using Rowles.Evolution.Random;
using Rowles.Evolution.Variation;

namespace Rowles.Evolution.Algorithms;

/// <summary>Deterministic, single-outstanding-batch Grid MAP-Elites scheduler.</summary>
public sealed class GridMapElites
{
    public const int CheckpointSchemaVersion = 1;
    private readonly GridMapElitesConfiguration configuration;
    private readonly GridArchive archive;
    private readonly EvolutionRandom random;
    private readonly IsoLineVariation variation;
    private NumericCandidate[]? pending;
    private long nextCandidateId;
    private long iteration;
    private long evaluations;
    private long insertions;
    private long replacements;
    private long invalidEvaluations;
    private readonly SortedDictionary<string, long> failureCounts = new(StringComparer.Ordinal);

    public GridMapElites(GridMapElitesConfiguration configuration, ulong seed)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        archive = new GridArchive(configuration.Archive);
        variation = configuration.Variation;
        random = new EvolutionRandom(seed);
    }

    private GridMapElites(GridMapElitesConfiguration configuration, EvolutionCheckpoint checkpoint)
        : this(configuration, 1)
    {
        random = new EvolutionRandom(checkpoint.RandomState);
        archive.Load(checkpoint.Elites);
        nextCandidateId = checkpoint.NextCandidateId;
        iteration = checkpoint.Iteration;
        evaluations = checkpoint.Evaluations;
        insertions = checkpoint.Insertions;
        replacements = checkpoint.Replacements;
        invalidEvaluations = checkpoint.InvalidEvaluations;
        foreach (FailureCountDto failure in checkpoint.FailureCounts)
        {
            if (string.IsNullOrWhiteSpace(failure.Reason) || failure.Count <= 0 || !failureCounts.TryAdd(failure.Reason, failure.Count))
                throw new ArgumentException("Checkpoint failure counters are invalid.", nameof(checkpoint));
        }
    }

    public GridArchive Archive => archive;
    public long Iteration => iteration;
    public long EvaluationsPerformed => evaluations;
    public bool HasOutstandingBatch => pending is not null;

    public IReadOnlyList<NumericCandidate> Ask(int batchSize)
    {
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (pending is not null) throw new InvalidOperationException("Tell the outstanding batch before asking for another.");
        GridElite[] parents = archive.OccupiedElites.ToArray();
        NumericCandidate[] batch = new NumericCandidate[batchSize];
        for (int i = 0; i < batch.Length; i++)
        {
            double[] solution;
            if (parents.Length == 0) solution = variation.CreateRandom(random);
            else
            {
                GridElite first = parents[random.NextInt32(parents.Length)];
                GridElite second = parents[random.NextInt32(parents.Length)];
                solution = variation.Mutate(first, second, random);
            }

            batch[i] = new NumericCandidate(nextCandidateId, solution, iteration);
            nextCandidateId = checked(nextCandidateId + 1);
        }

        pending = batch;
        return Array.AsReadOnly(batch);
    }

    public QdProgressMetrics Tell(IReadOnlyList<CandidateEvaluation> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        NumericCandidate[] expected = pending ?? throw new InvalidOperationException("There is no outstanding Ask batch.");
        if (results.Count != expected.Length) throw new ArgumentException("Tell must contain exactly one result for every candidate in the outstanding batch.", nameof(results));

        Dictionary<long, CandidateEvaluation> byId = new(expected.Length);
        foreach (CandidateEvaluation result in results)
        {
            if (result is null || !byId.TryAdd(result.CandidateId, result))
                throw new ArgumentException("Tell contains a null or duplicate candidate ID.", nameof(results));
            if (!expected.Any(candidate => candidate.CandidateId == result.CandidateId))
                throw new ArgumentException($"Candidate ID {result.CandidateId} does not belong to the outstanding batch.", nameof(results));
            ValidateResult(result);
        }

        foreach (NumericCandidate candidate in expected)
            if (!byId.ContainsKey(candidate.CandidateId))
                throw new ArgumentException($"Tell is missing candidate ID {candidate.CandidateId}.", nameof(results));

        foreach (NumericCandidate candidate in expected)
        {
            CandidateEvaluation result = byId[candidate.CandidateId];
            evaluations++;
            if (!result.IsValid)
            {
                invalidEvaluations++;
                string reason = result.FailureReason!;
                failureCounts[reason] = failureCounts.GetValueOrDefault(reason) + 1;
                continue;
            }

            if (archive.TryInsert(candidate.CandidateId, candidate.CopyValues(), result.Objective,
                result.Descriptors, iteration, out bool replaced))
            {
                insertions++;
                if (replaced) replacements++;
            }
        }

        pending = null;
        iteration = checked(iteration + 1);
        return GetMetrics();
    }

    public QdProgressMetrics GetMetrics()
    {
        double? qdScore = null;
        if (configuration.Archive.ObjectiveBaseline is double baseline)
        {
            double sum = 0;
            foreach (GridElite elite in archive.OccupiedElites)
                sum += configuration.Archive.ObjectiveDirection == ObjectiveDirection.Maximise
                    ? elite.Objective - baseline
                    : baseline - elite.Objective;
            qdScore = sum;
        }

        double? best = null;
        foreach (GridElite elite in archive.OccupiedElites)
            if (best is null || (configuration.Archive.ObjectiveDirection == ObjectiveDirection.Maximise
                ? elite.Objective > best.Value : elite.Objective < best.Value)) best = elite.Objective;
        return new QdProgressMetrics(iteration, evaluations, archive.Occupancy, archive.Coverage, qdScore,
            best, insertions, replacements, invalidEvaluations,
            new SortedDictionary<string, long>(failureCounts, StringComparer.Ordinal));
    }

    public string CreateCheckpointJson()
    {
        if (pending is not null) throw new InvalidOperationException("Checkpoints are available only at batch boundaries.");
        EvolutionCheckpoint checkpoint = new(CheckpointSchemaVersion,
            configuration.SolutionBounds.Select(b => new NumericBoundsDto(b.Lower, b.Upper)).ToArray(),
            configuration.Archive.LowerBounds.ToArray(), configuration.Archive.UpperBounds.ToArray(),
            configuration.Archive.BinCounts.ToArray(), configuration.Archive.ObjectiveDirection,
            configuration.Archive.ObjectiveBaseline, configuration.Archive.MaximumCells,
            variation.SigmaIso, variation.SigmaLine, random.Capture(), nextCandidateId, iteration,
            evaluations, insertions, replacements, invalidEvaluations,
            failureCounts.Select(pair => new FailureCountDto(pair.Key, pair.Value)).ToArray(), archive.ToDtos());
        return System.Text.Json.JsonSerializer.Serialize(checkpoint, EvolutionJsonContext.Default.EvolutionCheckpoint);
    }

    public static GridMapElites Restore(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        EvolutionCheckpoint checkpoint = System.Text.Json.JsonSerializer.Deserialize(json,
            EvolutionJsonContext.Default.EvolutionCheckpoint) ?? throw new System.Text.Json.JsonException("Checkpoint was JSON null.");
        if (checkpoint.SchemaVersion != CheckpointSchemaVersion)
            throw new NotSupportedException($"Evolution checkpoint schema {checkpoint.SchemaVersion} is unsupported.");
        ValidateCheckpoint(checkpoint);
        GridArchiveConfiguration archive = new(checkpoint.DescriptorLowerBounds, checkpoint.DescriptorUpperBounds,
            checkpoint.BinCounts, checkpoint.ObjectiveDirection, checkpoint.ObjectiveBaseline, checkpoint.MaximumCells);
        GridMapElitesConfiguration configuration = new(checkpoint.SolutionBounds.Select(b => new NumericBounds(b.Lower, b.Upper)),
            archive, checkpoint.SigmaIso, checkpoint.SigmaLine);
        return new GridMapElites(configuration, checkpoint);
    }

    private void ValidateResult(CandidateEvaluation result)
    {
        if (!result.IsValid)
        {
            if (string.IsNullOrWhiteSpace(result.FailureReason)) throw new ArgumentException("Invalid evaluations require a failure reason.");
            return;
        }

        if (!double.IsFinite(result.Objective) || result.Descriptors.Count != configuration.Archive.Dimension)
            throw new ArgumentException("Valid evaluation objective and descriptor dimensions must be finite and match the archive.");
        _ = configuration.Archive.GetCellIndex(result.Descriptors);
    }

    private static void ValidateCheckpoint(EvolutionCheckpoint checkpoint)
    {
        if (checkpoint.SolutionBounds is null || checkpoint.SolutionBounds.Length == 0 ||
            checkpoint.DescriptorLowerBounds is null || checkpoint.DescriptorUpperBounds is null ||
            checkpoint.BinCounts is null || checkpoint.FailureCounts is null || checkpoint.Elites is null ||
            checkpoint.NextCandidateId < 0 || checkpoint.Iteration < 0 || checkpoint.Evaluations < 0 ||
            checkpoint.Insertions < 0 || checkpoint.Replacements < 0 || checkpoint.InvalidEvaluations < 0 ||
            checkpoint.RandomState is null || checkpoint.NextCandidateId != checkpoint.Evaluations)
            throw new ArgumentException("Checkpoint counters or arrays are invalid.", nameof(checkpoint));
    }
}
