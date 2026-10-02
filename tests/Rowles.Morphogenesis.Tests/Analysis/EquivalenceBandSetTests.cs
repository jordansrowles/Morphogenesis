using System.Text.Json;
using Rowles.Morphogenesis.Benchmarks.Analysis;

namespace Rowles.Morphogenesis.Tests.Analysis;

public sealed class EquivalenceBandSetTests
{
    [Fact]
    public void Frozen_band_set_round_trips_and_has_a_deterministic_payload_hash()
    {
        EquivalenceBand[] bands = CreateBands();
        EquivalenceBandSet first = EquivalenceBandSet.Create(new string('A', 64), new string('B', 64), bands);
        EquivalenceBandSet second = EquivalenceBandSet.Create(new string('A', 64), new string('B', 64), bands.Reverse().ToArray());
        Assert.Equal(first.BandPayloadSha256, second.BandPayloadSha256);

        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, EquivalenceBandSet.FileName);
        first.Save(path);
        EquivalenceBandSet restored = EquivalenceBandSet.Load(path);
        Assert.Equal(first.BandPayloadSha256, restored.BandPayloadSha256);
        Assert.Equal(first.Bands, restored.Bands);
        Assert.Equal(EventEnsembleProtocol.Version, restored.AnalysisProtocolVersion);
    }

    [Fact]
    public void Tampered_payload_is_rejected()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, EquivalenceBandSet.FileName);
        EquivalenceBandSet.Create(new string('A', 64), new string('B', 64), CreateBands()).Save(path);
        string json = File.ReadAllText(path).Replace("\"BandPayloadSha256\": \"", "\"BandPayloadSha256\": \"F", StringComparison.Ordinal);
        File.WriteAllText(path, json);
        Assert.Throws<InvalidDataException>(() => EquivalenceBandSet.Load(path));
    }

    [Fact]
    public void Protocol_version_and_invalid_hashes_are_rejected()
    {
        EquivalenceBandSet valid = EquivalenceBandSet.Create(new string('A', 64), new string('B', 64), CreateBands());
        Assert.Throws<InvalidDataException>(() => (valid with { AnalysisProtocolVersion = EventEnsembleProtocol.Version + 1 }).ValidateIntegrity());
        Assert.Throws<FormatException>(() => (valid with { QualificationProtocolSha256 = "not-a-hash" }).ValidateIntegrity());
        Assert.Throws<FormatException>(() => (valid with { CanonicalCaptureSha256 = "not-a-hash" }).ValidateIntegrity());
    }

    [Fact]
    public void Zero_variation_produces_an_exact_zero_width_band()
    {
        EquivalenceBand band = CreateBands().First(item => item.Condition == EventEnsembleProtocol.Conditions[0] &&
            item.Mcs == 0 && item.Metric == EventEnsembleProtocol.Metrics[0]);
        Assert.Equal(band.CanonicalMean, band.LowerBound);
        Assert.Equal(band.CanonicalMean, band.UpperBound);
    }

    private static EquivalenceBand[] CreateBands() => EventEnsembleProtocol.Conditions
        .SelectMany(condition => EventEnsembleProtocol.Checkpoints(condition)
            .SelectMany(mcs => EventEnsembleProtocol.Metrics.Select(metric =>
                new EquivalenceBand(condition, mcs, metric, 64, 3, 0, 3, 3))))
        .ToArray();
}

internal sealed class TemporaryDirectory : IDisposable
{
    internal TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"m3-analysis-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    internal string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
