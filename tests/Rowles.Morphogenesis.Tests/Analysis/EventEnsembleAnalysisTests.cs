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
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
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
    }

    [Fact]
    public void Changed_protocol_file_is_rejected()
    {
        using AnalysisFixture fixture = AnalysisFixture.Create();
        File.AppendAllText(System.IO.Path.Combine(fixture.Path, "qualification-protocol.md"), "Changed after freezing.\n");
        EnsembleQualificationResult result = EventEnsembleAnalysis.Compare("border", fixture.Path, 64);
        Assert.False(result.Passed);
        Assert.Contains(result.FailureDescriptions, failure => failure.Contains("protocol file SHA-256", StringComparison.Ordinal));
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
}

internal sealed class AnalysisFixture : IDisposable
{
    private AnalysisFixture(string path) => Path = path;

    internal string Path { get; }

    internal static AnalysisFixture Create(Func<string, int, int, string, double, double>? change = null)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"m3-workflow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        AnalysisFixture fixture = new(path);
        string protocolPath = System.IO.Path.Combine(path, "protocol.md");
        File.WriteAllText(protocolPath, "Frozen synthetic event qualification protocol v1\n");
        WriteCapture(path, "canonical", change: null);
        WriteManifests(path, "canonical");
        EventEnsembleAnalysis.Freeze(path, protocolPath);
        foreach (string kernel in new[] { "border", "directed" })
        {
            WriteCapture(path, kernel, change);
            WriteManifests(path, kernel);
        }
        return fixture;
    }

    private static void WriteCapture(string root, string kernel, Func<string, int, int, string, double, double>? change)
    {
        string path = System.IO.Path.Combine(root, $"{kernel}-samples-64.csv");
        using StreamWriter writer = new(path);
        string[] headers = ["condition", "kernel", "replicate", "initialisation_seed", "dynamics_seed", "initial_hash", "mcs", .. EventEnsembleProtocol.Metrics];
        writer.WriteLine(string.Join(',', headers));
        foreach (string condition in EventEnsembleProtocol.Conditions)
        {
            foreach (int mcs in EventEnsembleProtocol.Checkpoints(condition))
            {
                for (int replicate = 0; replicate < 64; replicate++)
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
                        if (change is not null) value = change(condition, mcs, replicate, metric, value);
                        fields.Add(value.ToString("R", CultureInfo.InvariantCulture));
                    }
                    writer.WriteLine(string.Join(',', fields));
                }
            }
        }
    }

    private static void WriteManifests(string root, string kernel)
    {
        foreach (string condition in EventEnsembleProtocol.Conditions)
        {
            string name = $"{condition}-{kernel}-manifest-64.json";
            string manifest = JsonSerializer.Serialize(new
            {
                Kernel = kernel,
                Configuration = new { ExperimentId = condition, GridWidth = 32, Name = "synthetic" }
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            File.WriteAllText(System.IO.Path.Combine(root, name), manifest);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
