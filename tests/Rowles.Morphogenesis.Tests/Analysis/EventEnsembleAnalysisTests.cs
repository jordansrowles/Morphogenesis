using System.Globalization;
using System.Text.Json;
using Rowles.Morphogenesis.Benchmarks.Analysis;

namespace Rowles.Morphogenesis.Tests.Analysis;

public sealed class EventEnsembleAnalysisTests
{
    [Fact]
    public void Complete_paired_capture_passes_and_repeated_qualification_is_deterministic()
    {
        using AnalysisFixture fixture = AnalysisFixture.Create();
        EnsembleQualificationResult first = EventEnsembleAnalysis.Compare("border", fixture.Path, 64);
        EnsembleQualificationResult second = EventEnsembleAnalysis.Compare("border", fixture.Path, 64);
        Assert.True(first.Passed, string.Join(Environment.NewLine, first.FailureDescriptions));
        Assert.Equal(682, first.MeanMetricsEvaluated);
        Assert.Equal(EventEnsembleAnalysis.ExpectedFinalVarianceMetricCount, first.VarianceMetricsEvaluated);
        Assert.Equal(0, first.MeanEquivalenceFailures);
        Assert.Equal(0, first.VarianceRatioFailures);
        AssertSummaryIdentities(first, 64);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Theory]
    [InlineData(128, "border")]
    [InlineData(128, "directed")]
    [InlineData(256, "border")]
    [InlineData(256, "directed")]
    public void Expanded_captures_pass_and_keep_deterministic_provenance(int seedCount, string kernel)
    {
        using AnalysisFixture fixture = AnalysisFixture.Create();
        string[] prefix64 = File.ReadAllLines(System.IO.Path.Combine(fixture.Path, "canonical-samples-64.csv"));
        fixture.ExpandTo(seedCount);
        string[] expanded = File.ReadAllLines(System.IO.Path.Combine(fixture.Path, $"canonical-samples-{seedCount}.csv"));
        string[] expandedPrefix = [expanded[0], .. expanded.Skip(1).Where(line =>
            int.Parse(line.Split(',')[2], CultureInfo.InvariantCulture) < 64)];
        Assert.Equal(prefix64, expandedPrefix);

        EnsembleQualificationResult first = EventEnsembleAnalysis.Compare(kernel, fixture.Path, seedCount);
        EnsembleQualificationResult second = EventEnsembleAnalysis.Compare(kernel, fixture.Path, seedCount);
        Assert.True(first.Passed, string.Join(Environment.NewLine, first.FailureDescriptions));
        AssertSummaryIdentities(first, seedCount);
        Assert.Equal(first.FrozenCanonicalCaptureSha256, second.FrozenCanonicalCaptureSha256);
        Assert.Equal(first.CanonicalCaptureSha256, second.CanonicalCaptureSha256);
        Assert.Equal(first.CandidateCaptureSha256, second.CandidateCaptureSha256);
        Assert.Equal(first.BandPayloadSha256, second.BandPayloadSha256);
        Assert.Equal(first.QualificationProtocolSha256, second.QualificationProtocolSha256);
        Assert.Equal(first.AnalysisProtocolSha256, second.AnalysisProtocolSha256);
    }

    [Fact]
    public void Expanded_canonical_configuration_may_change_only_replicate_count()
    {
        using AnalysisFixture fixture = AnalysisFixture.Create();
        fixture.ExpandTo(128);
        string canonical64 = System.IO.Path.Combine(fixture.Path, "sorting-32-t6-canonical-manifest-64.json");
        string canonical128 = System.IO.Path.Combine(fixture.Path, "sorting-32-t6-canonical-manifest-128.json");
        Assert.NotEqual(EventEnsembleAnalysis.ConfigurationIdentity(canonical64),
            EventEnsembleAnalysis.ConfigurationIdentity(canonical128));
        Assert.Equal(EventEnsembleAnalysis.ScientificConfigurationIdentity(canonical64),
            EventEnsembleAnalysis.ScientificConfigurationIdentity(canonical128));
        Assert.True(EventEnsembleAnalysis.Compare("border", fixture.Path, 128).Passed);
    }

    [Fact]
    public void Expanded_canonical_configuration_drift_is_rejected_even_when_candidate_matches_it()
    {
        using AnalysisFixture fixture = AnalysisFixture.Create();
        fixture.ExpandTo(128);
        foreach (string kernel in new[] { "canonical", "border" })
        {
            string path = System.IO.Path.Combine(fixture.Path, $"sorting-32-t6-{kernel}-manifest-128.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"gridWidth\":32", "\"gridWidth\":64", StringComparison.Ordinal));
        }

        EnsembleQualificationResult result = EventEnsembleAnalysis.Compare("border", fixture.Path, 128);
        Assert.False(result.Passed);
        Assert.Contains(result.FailureDescriptions, failure => failure.Contains(
            "Canonical scientific configuration differs from the frozen 64-seed configuration", StringComparison.Ordinal));
    }

    [Fact]
    public void Expanded_candidate_configuration_must_match_current_canonical_configuration()
    {
        using AnalysisFixture fixture = AnalysisFixture.Create();
        fixture.ExpandTo(128);
        string path = System.IO.Path.Combine(fixture.Path, "sorting-32-t6-border-manifest-128.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"gridWidth\":32", "\"gridWidth\":64", StringComparison.Ordinal));

        EnsembleQualificationResult result = EventEnsembleAnalysis.Compare("border", fixture.Path, 128);
        Assert.False(result.Passed);
        Assert.Contains(result.FailureDescriptions, failure => failure.Contains("configuration differs", StringComparison.Ordinal));
    }

    [Fact]
    public void Expanded_canonical_prefix_tampering_is_rejected()
    {
        using AnalysisFixture fixture = AnalysisFixture.Create();
        fixture.ExpandTo(128);
        string path = System.IO.Path.Combine(fixture.Path, "canonical-samples-128.csv");
        string[] lines = File.ReadAllLines(path);
        string[] headers = lines[0].Split(',');
        int metricColumn = Array.IndexOf(headers, "area_mean");
        int rowIndex = Array.FindIndex(lines, line =>
        {
            string[] fields = line.Split(',');
            return fields[0] == "sorting-32-t6" && fields[2] == "0" && fields[6] == "10";
        });
        Assert.True(rowIndex > 0);
        string[] row = lines[rowIndex].Split(',');
        row[metricColumn] = "999";
        lines[rowIndex] = string.Join(',', row);
        File.WriteAllLines(path, lines);

        EnsembleQualificationResult result = EventEnsembleAnalysis.Compare("border", fixture.Path, 128);
        Assert.False(result.Passed);
        Assert.Contains(result.FailureDescriptions, failure => failure.Contains("Canonical seed prefix changed", StringComparison.Ordinal));
    }

    [Fact]
    public void Mean_equivalence_failure_is_reported()
    {
        using AnalysisFixture fixture = AnalysisFixture.Create((condition, mcs, _, metric, value) =>
            metric == "area_mean" && mcs > 0 ? value + 30 : value);
        EnsembleQualificationResult result = EventEnsembleAnalysis.Compare("border", fixture.Path, 64);
        Assert.False(result.Passed);
        Assert.True(result.MeanEquivalenceFailures > 0);
    }

    [Fact]
    public void Final_variance_failure_is_reported_while_the_mean_can_pass()
    {
        using AnalysisFixture fixture = AnalysisFixture.Create((condition, mcs, _, metric, value) =>
        {
            if (condition != "sorting-32-t6" || mcs != 80 || metric != "area_mean") return value;
            double mean = 31.5;
            return mean + 3 * (value - mean);
        });
        EnsembleQualificationResult result = EventEnsembleAnalysis.Compare("border", fixture.Path, 64);
        Assert.False(result.Passed);
        Assert.True(result.VarianceRatioFailures > 0);
    }

    [Fact]
    public void Checkpoint_zero_mismatch_is_reported()
    {
        using AnalysisFixture fixture = AnalysisFixture.Create((condition, mcs, replicate, metric, value) =>
            mcs == 0 && replicate == 0 && metric == "mixing" ? value + 0.1 : value);
        EnsembleQualificationResult result = EventEnsembleAnalysis.Compare("border", fixture.Path, 64);
        Assert.False(result.Passed);
        Assert.True(result.CheckpointZeroFailures > 0);
    }

    [Fact]
    public void Missing_seed_duplicate_seed_and_missing_metric_are_rejected()
    {
        using AnalysisFixture missing = AnalysisFixture.Create();
        string capture = System.IO.Path.Combine(missing.Path, "border-samples-64.csv");
        string[] lines = File.ReadAllLines(capture);
        File.WriteAllLines(capture, lines[..^1]);
        Assert.Throws<InvalidDataException>(() => EventEnsembleAnalysis.Compare("border", missing.Path, 64));

        using AnalysisFixture duplicate = AnalysisFixture.Create();
        capture = System.IO.Path.Combine(duplicate.Path, "border-samples-64.csv");
        File.AppendAllLines(capture, [File.ReadLines(capture).Last()]);
        Assert.Throws<InvalidDataException>(() => EventEnsembleAnalysis.Compare("border", duplicate.Path, 64));

        using AnalysisFixture metric = AnalysisFixture.Create();
        capture = System.IO.Path.Combine(metric.Path, "border-samples-64.csv");
        File.WriteAllText(capture, File.ReadAllText(capture).Replace(",shape_mean,", ",", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => EventEnsembleAnalysis.Compare("border", metric.Path, 64));
    }

    [Fact]
    public void Changed_paired_seed_between_checkpoints_is_rejected()
    {
        using AnalysisFixture fixture = AnalysisFixture.Create();
        string path = System.IO.Path.Combine(fixture.Path, "border-samples-64.csv");
        string[] lines = File.ReadAllLines(path);
        int row = Array.FindIndex(lines, line =>
        {
            string[] fields = line.Split(',');
            return fields.Length > 6 && fields[0] == "sorting-32-t6" && fields[1] == "border" &&
                   fields[2] == "0" && fields[6] == "10";
        });
        Assert.True(row > 0);
        string[] altered = lines[row].Split(',');
        altered[3] = "999999";
        lines[row] = string.Join(',', altered);
        File.WriteAllLines(path, lines);
        Assert.Throws<InvalidDataException>(() => EventEnsembleAnalysis.Compare("border", fixture.Path, 64));
    }

    [Theory]
    [InlineData("BandPayloadSha256", "0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("AnalysisProtocolVersion", "99")]
    [InlineData("CanonicalCaptureSha256", "0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("QualificationProtocolSha256", "0000000000000000000000000000000000000000000000000000000000000000")]
    public void Provenance_mismatch_fails_before_statistical_comparison(string field, string replacement)
    {
        using AnalysisFixture fixture = AnalysisFixture.Create();
        string path = System.IO.Path.Combine(fixture.Path, EquivalenceBandSet.FileName);
        string json = File.ReadAllText(path);
        int start = json.IndexOf($"\"{field}\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int valueStart = json.IndexOf(':', start) + 1;
        while (char.IsWhiteSpace(json[valueStart])) valueStart++;
        int valueEnd = json[valueStart] == '"' ? json.IndexOf('"', valueStart + 1) + 1 : json.IndexOfAny([',', '\n'], valueStart);
        string altered = json[..valueStart] + (json[valueStart] == '"' ? $"\"{replacement}\"" : replacement) + json[valueEnd..];
        File.WriteAllText(path, altered);
        EnsembleQualificationResult result = EventEnsembleAnalysis.Compare("border", fixture.Path, 64);
        Assert.False(result.Passed);
        Assert.True(result.ProvenanceFailures > 0);
        Assert.Equal(0, result.MeanMetricsEvaluated);
        Assert.NotNull(result.AnalysisProtocolSha256);
        Assert.Null(result.FrozenCanonicalCaptureSha256);
        Assert.Null(result.BandPayloadSha256);
        Assert.Null(result.QualificationProtocolSha256);
        Assert.NotNull(result.CanonicalCaptureSha256);
        Assert.NotNull(result.CandidateCaptureSha256);
    }

    [Fact]
    public void Changed_protocol_file_is_rejected()
    {
        using AnalysisFixture fixture = AnalysisFixture.Create();
        File.AppendAllText(System.IO.Path.Combine(fixture.Path, "qualification-protocol.md"), "Changed after freezing.\n");
        EnsembleQualificationResult result = EventEnsembleAnalysis.Compare("border", fixture.Path, 64);
        Assert.False(result.Passed);
        Assert.Contains(result.FailureDescriptions, failure => failure.Contains("protocol file SHA-256", StringComparison.Ordinal));
        Assert.NotNull(result.FrozenCanonicalCaptureSha256);
        Assert.NotNull(result.BandPayloadSha256);
        Assert.Null(result.QualificationProtocolSha256);
        Assert.NotNull(result.AnalysisProtocolSha256);
        Assert.NotNull(result.CanonicalCaptureSha256);
        Assert.NotNull(result.CandidateCaptureSha256);
    }

    [Fact]
    public void Changed_canonical_capture_is_rejected_by_its_frozen_identity()
    {
        using AnalysisFixture fixture = AnalysisFixture.Create();
        string path = System.IO.Path.Combine(fixture.Path, "canonical-samples-64.csv");
        File.WriteAllText(path, File.ReadAllText(path).Replace(",0.5,", ",0.6,", StringComparison.Ordinal));
        EnsembleQualificationResult result = EventEnsembleAnalysis.Compare("border", fixture.Path, 64);
        Assert.False(result.Passed);
        Assert.Contains(result.FailureDescriptions, failure => failure.Contains("Canonical capture SHA-256", StringComparison.Ordinal));
    }

    [Fact]
    public void Different_kernel_manifest_configuration_is_rejected()
    {
        using AnalysisFixture fixture = AnalysisFixture.Create();
        string path = System.IO.Path.Combine(fixture.Path, "sorting-32-t6-border-manifest-64.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"gridWidth\":32", "\"gridWidth\":64", StringComparison.Ordinal));
        EnsembleQualificationResult result = EventEnsembleAnalysis.Compare("border", fixture.Path, 64);
        Assert.False(result.Passed);
        Assert.Contains(result.FailureDescriptions, failure => failure.Contains("configuration differs", StringComparison.Ordinal));
        Assert.NotNull(result.CanonicalCaptureSha256);
        Assert.Null(result.CandidateCaptureSha256);
    }

    [Fact]
    public void Resolved_manifest_configuration_identity_ignores_kernel_metadata()
    {
        using TemporaryDirectory directory = new();
        string canonical = System.IO.Path.Combine(directory.Path, "canonical.json");
        string candidate = System.IO.Path.Combine(directory.Path, "candidate.json");
        File.WriteAllText(canonical, "{\"kernel\":\"canonical\",\"configuration\":{\"gridWidth\":32,\"contactEnergies\":[[0,1],[1,0]]}}");
        File.WriteAllText(candidate, "{\"kernel\":\"border\",\"configuration\":{\"contactEnergies\":[[0,1],[1,0]],\"gridWidth\":32}}");
        Assert.Equal(EventEnsembleAnalysis.ConfigurationIdentity(canonical), EventEnsembleAnalysis.ConfigurationIdentity(candidate));
        File.WriteAllText(candidate, "{\"kernel\":\"directed\",\"configuration\":{\"contactEnergies\":[[0,1],[1,0]],\"gridWidth\":64}}");
        Assert.NotEqual(EventEnsembleAnalysis.ConfigurationIdentity(canonical), EventEnsembleAnalysis.ConfigurationIdentity(candidate));
    }

    [Fact]
    public void Scientific_configuration_identity_excludes_only_top_level_replicate_count()
    {
        using TemporaryDirectory directory = new();
        string canonical = System.IO.Path.Combine(directory.Path, "canonical.json");
        string expanded = System.IO.Path.Combine(directory.Path, "expanded.json");
        File.WriteAllText(canonical,
            "{\"kernel\":\"canonical\",\"configuration\":{\"replicateCount\":64,\"experimentId\":\"same\",\"nested\":{\"replicateCount\":4},\"values\":[1,2]}}");
        File.WriteAllText(expanded,
            "{\"kernel\":\"canonical\",\"configuration\":{\"replicateCount\":128,\"experimentId\":\"same\",\"nested\":{\"replicateCount\":4},\"values\":[1,2]}}");
        Assert.Equal(EventEnsembleAnalysis.ScientificConfigurationIdentity(canonical),
            EventEnsembleAnalysis.ScientificConfigurationIdentity(expanded));
        Assert.NotEqual(EventEnsembleAnalysis.ConfigurationIdentity(canonical),
            EventEnsembleAnalysis.ConfigurationIdentity(expanded));

        File.WriteAllText(expanded,
            "{\"kernel\":\"canonical\",\"configuration\":{\"replicateCount\":128,\"experimentId\":\"same\",\"nested\":{\"replicateCount\":5},\"values\":[1,2]}}");
        Assert.NotEqual(EventEnsembleAnalysis.ScientificConfigurationIdentity(canonical),
            EventEnsembleAnalysis.ScientificConfigurationIdentity(expanded));
    }

    [Fact]
    public void Comparison_requires_only_the_selected_kernel_manifest_and_checks_its_name()
    {
        using AnalysisFixture fixture = AnalysisFixture.Create();
        foreach (string path in Directory.GetFiles(fixture.Path, "*-directed-manifest-64.json")) File.Delete(path);
        Assert.True(EventEnsembleAnalysis.Compare("border", fixture.Path, 64).Passed);

        string manifest = Directory.GetFiles(fixture.Path, "*-border-manifest-64.json")[0];
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"kernel\":\"border\"", "\"kernel\":\"directed\"", StringComparison.Ordinal));
        EnsembleQualificationResult result = EventEnsembleAnalysis.Compare("border", fixture.Path, 64);
        Assert.False(result.Passed);
        Assert.Contains(result.FailureDescriptions, failure => failure.Contains("kernel is incorrect", StringComparison.Ordinal));
    }

    [Fact]
    public void Freeze_rejects_accelerated_captures_at_any_seed_count()
    {
        using AnalysisFixture fixture = AnalysisFixture.Create();
        File.Delete(System.IO.Path.Combine(fixture.Path, EquivalenceBandSet.FileName));
        File.Move(System.IO.Path.Combine(fixture.Path, "border-samples-64.csv"),
            System.IO.Path.Combine(fixture.Path, "border-samples-128.csv"));
        Assert.Throws<InvalidOperationException>(() => EventEnsembleAnalysis.Freeze(
            fixture.Path, System.IO.Path.Combine(fixture.Path, "protocol.md")));
    }

    private static void AssertSummaryIdentities(EnsembleQualificationResult result, int seedCount)
    {
        string?[] identities =
        [
            result.FrozenCanonicalCaptureSha256,
            result.CanonicalCaptureSha256,
            result.CandidateCaptureSha256,
            result.BandPayloadSha256,
            result.QualificationProtocolSha256,
            result.AnalysisProtocolSha256
        ];
        foreach (string? identity in identities)
        {
            Assert.NotNull(identity);
            Assert.Equal(32, Convert.FromHexString(identity!).Length);
        }

        Assert.Equal(EventEnsembleProtocol.AnalysisProtocolSha256, result.AnalysisProtocolSha256);
        if (seedCount == 64)
            Assert.Equal(result.FrozenCanonicalCaptureSha256, result.CanonicalCaptureSha256);
        else
            Assert.NotEqual(result.FrozenCanonicalCaptureSha256, result.CanonicalCaptureSha256);
    }
}

internal sealed class AnalysisFixture : IDisposable
{
    private readonly Func<string, int, int, string, double, double>? _candidateChange;

    private AnalysisFixture(string path, Func<string, int, int, string, double, double>? candidateChange)
    {
        Path = path;
        _candidateChange = candidateChange;
    }

    internal string Path { get; }

    internal static AnalysisFixture Create(Func<string, int, int, string, double, double>? change = null)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"m3-workflow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        AnalysisFixture fixture = new(path, change);
        string protocolPath = System.IO.Path.Combine(path, "protocol.md");
        File.WriteAllText(protocolPath, "Frozen synthetic event qualification protocol v1\n");
        fixture.WriteCapture("canonical", 64);
        fixture.WriteManifests("canonical", 64);
        EventEnsembleAnalysis.Freeze(path, protocolPath);
        foreach (string kernel in new[] { "border", "directed" })
        {
            fixture.WriteCapture(kernel, 64);
            fixture.WriteManifests(kernel, 64);
        }
        return fixture;
    }

    internal void ExpandTo(int seedCount)
    {
        if (seedCount is not (128 or 256)) throw new ArgumentOutOfRangeException(nameof(seedCount));
        foreach (string kernel in new[] { "canonical", "border", "directed" })
        {
            WriteCapture(kernel, seedCount);
            WriteManifests(kernel, seedCount);
        }
    }

    internal void WriteCapture(string kernel, int seedCount,
        Func<string, int, int, string, double, double>? change = null)
    {
        string path = System.IO.Path.Combine(Path, $"{kernel}-samples-{seedCount}.csv");
        Func<string, int, int, string, double, double>? rowChange = kernel == "canonical" ? null : change ?? _candidateChange;
        using StreamWriter writer = new(path);
        string[] headers = ["condition", "kernel", "replicate", "initialisation_seed", "dynamics_seed", "initial_hash", "mcs", .. EventEnsembleProtocol.Metrics];
        writer.WriteLine(string.Join(',', headers));
        foreach (string condition in EventEnsembleProtocol.Conditions)
        {
            foreach (int mcs in EventEnsembleProtocol.Checkpoints(condition))
            {
                for (int replicate = 0; replicate < seedCount; replicate++)
                {
                    List<string> fields =
                    [
                        condition,
                        kernel,
                        replicate.ToString(CultureInfo.InvariantCulture),
                        (100000UL + (ulong)replicate).ToString(CultureInfo.InvariantCulture),
                        (200000UL + (ulong)replicate).ToString(CultureInfo.InvariantCulture),
                        (0xABCDEFUL + (ulong)replicate).ToString("X16", CultureInfo.InvariantCulture),
                        mcs.ToString(CultureInfo.InvariantCulture)
                    ];
                    foreach (string metric in EventEnsembleProtocol.Metrics)
                    {
                        double value = metric == "area_mean" ? replicate : 1;
                        if (mcs == 0 && metric == "mixing") value = 0.5;
                        if (rowChange is not null) value = rowChange(condition, mcs, replicate, metric, value);
                        fields.Add(value.ToString("R", CultureInfo.InvariantCulture));
                    }
                    writer.WriteLine(string.Join(',', fields));
                }
            }
        }
    }

    internal void WriteManifests(string kernel, int seedCount)
    {
        foreach (string condition in EventEnsembleProtocol.Conditions)
        {
            string name = $"{condition}-{kernel}-manifest-{seedCount}.json";
            string manifest = JsonSerializer.Serialize(new
            {
                Kernel = kernel,
                Configuration = new
                {
                    ExperimentId = condition,
                    GridWidth = 32,
                    Name = "synthetic",
                    ReplicateCount = seedCount,
                    Nested = new { replicateCount = 4 }
                }
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            File.WriteAllText(System.IO.Path.Combine(Path, name), manifest);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
