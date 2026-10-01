using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using Rowles.Evolution.Algorithms;
using Rowles.Evolution.Archives;
using Rowles.Evolution.Evaluation;
using Rowles.Evolution.Metrics;
using Rowles.Evolution.Variation;
using Rowles.Morphogenesis.Optimisation;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
RunSyntheticBaseline();
if (!args.Contains("--synthetic-only", StringComparer.Ordinal)) RunMorphogenesisCampaign();

static void RunSyntheticBaseline()
{
    const int batchSize = 64;
    const int batchCount = 128;
    long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
    GridMapElites algorithm = CreateAlgorithm(20260929);
    double askMilliseconds = 0;
    double tellMilliseconds = 0;
    Stopwatch wall = Stopwatch.StartNew();
    for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
    {
        Stopwatch phase = Stopwatch.StartNew();
        IReadOnlyList<NumericCandidate> batch = algorithm.Ask(batchSize);
        askMilliseconds += phase.Elapsed.TotalMilliseconds;
        CandidateEvaluation[] values = batch.Select(candidate => CandidateEvaluation.Valid(candidate.CandidateId,
            1 - candidate.Values.Sum(value => value * value), candidate.Values)).ToArray();
        phase.Restart();
        algorithm.Tell(values);
        tellMilliseconds += phase.Elapsed.TotalMilliseconds;
    }
    wall.Stop();
    Stopwatch checkpointWatch = Stopwatch.StartNew();
    string checkpoint = algorithm.CreateCheckpointJson();
    checkpointWatch.Stop();
    long allocated = Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore);
    string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(checkpoint)));
    Console.WriteLine($"synthetic evaluations={algorithm.EvaluationsPerformed} batches={batchCount} archive={algorithm.Archive.Occupancy}/{algorithm.Archive.Configuration.CellCount} wall_ms={wall.Elapsed.TotalMilliseconds:F3} eval_per_sec={algorithm.EvaluationsPerformed / wall.Elapsed.TotalSeconds:F1} allocated_bytes={allocated} ask_variation_ms={askMilliseconds:F3} tell_archive_metrics_ms={tellMilliseconds:F3} checkpoint_bytes={Encoding.UTF8.GetByteCount(checkpoint)} checkpoint_ms={checkpointWatch.Elapsed.TotalMilliseconds:F3} best={algorithm.GetMetrics().BestObjective:F6} archive_checkpoint_sha256={hash}");
}

static void RunMorphogenesisCampaign()
{
    const int batchSize = 16;
    const int batchCount = 8;
    long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
    MorphogenesisCandidateEvaluator evaluator = new(MorphogenesisCandidateEvaluator.CreateE02Template());
    GridMapElites algorithm = CreateMorphogenesisAlgorithm(20260929);
    List<double> durations = new(batchSize * batchCount);
    long replicateCount = 0;
    double askMilliseconds = 0;
    double tellMilliseconds = 0;
    Stopwatch campaign = Stopwatch.StartNew();
    for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
    {
        Stopwatch phase = Stopwatch.StartNew();
        IReadOnlyList<NumericCandidate> batch = algorithm.Ask(batchSize);
        askMilliseconds += phase.Elapsed.TotalMilliseconds;
        MorphogenesisEvaluationResult[] values = new MorphogenesisEvaluationResult[batch.Count];
        for (int i = 0; i < batch.Count; i++)
        {
            values[i] = evaluator.Evaluate(batch[i]);
            durations.Add(values[i].ElapsedMilliseconds);
            replicateCount += values[i].ReplicateElapsedMilliseconds.Length;
        }

        phase.Restart();
        algorithm.Tell(values.Select(value => value.Evaluation).Reverse().ToArray());
        tellMilliseconds += phase.Elapsed.TotalMilliseconds;
    }
    campaign.Stop();
    QdProgressMetrics metrics = algorithm.GetMetrics();
    double[] ordered = durations.Order().ToArray();
    double mean = ordered.Average();
    double p95 = ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1];
    Stopwatch checkpointWatch = Stopwatch.StartNew();
    string checkpoint = algorithm.CreateCheckpointJson();
    checkpointWatch.Stop();
    long allocatedBytes = Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore);
    Console.WriteLine($"morphogenesis candidates={durations.Count} batches={batchCount} replicates={replicateCount} archive={algorithm.Archive.Occupancy}/256 invalid={metrics.InvalidEvaluations} invalid_reasons={string.Join(";", metrics.FailureCounts.Select(pair => $"{pair.Key}:{pair.Value}"))} wall_ms={campaign.Elapsed.TotalMilliseconds:F3} candidate_per_sec={durations.Count / campaign.Elapsed.TotalSeconds:F3} replicate_per_sec={replicateCount / campaign.Elapsed.TotalSeconds:F3} mean_eval_ms={mean:F3} p95_eval_ms={p95:F3} allocated_bytes={allocatedBytes} ask_variation_ms={askMilliseconds:F3} tell_archive_metrics_ms={tellMilliseconds:F3} checkpoint_bytes={Encoding.UTF8.GetByteCount(checkpoint)} checkpoint_ms={checkpointWatch.Elapsed.TotalMilliseconds:F3}");
    if (durations.Count != 128 || replicateCount != 256 || algorithm.Archive.Occupancy == 0)
        throw new InvalidOperationException("The fixed eight-batch integration campaign did not complete its acceptance workload.");
}

static GridMapElites CreateAlgorithm(ulong seed) => new(new GridMapElitesConfiguration(
    [new NumericBounds(-1, 1), new NumericBounds(-1, 1)],
    new GridArchiveConfiguration([-1, -1], [1, 1], [16, 16], ObjectiveDirection.Maximise, 0)), seed);

static GridMapElites CreateMorphogenesisAlgorithm(ulong seed) => new(new GridMapElitesConfiguration(
    [new NumericBounds(0, 20), new NumericBounds(0, 30), new NumericBounds(0, 24)],
    new GridArchiveConfiguration([0, 0], [1, 1], [16, 16], ObjectiveDirection.Maximise, 0)), seed);
