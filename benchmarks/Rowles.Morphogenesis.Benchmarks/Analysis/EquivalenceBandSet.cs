using System.Security.Cryptography;
using System.Text.Json;

namespace Rowles.Morphogenesis.Benchmarks.Analysis;

internal sealed record EquivalenceMetricDefinition(string Name, string Definition);

internal sealed record EquivalenceBand(
    string Condition,
    int Mcs,
    string Metric,
    int SeedCount,
    double CanonicalMean,
    double CanonicalSampleStandardDeviation,
    double LowerBound,
    double UpperBound);

internal sealed record EquivalenceBandPayload(
    int FormatVersion,
    int AnalysisProtocolVersion,
    string CanonicalCaptureSha256,
    string QualificationProtocolSha256,
    int SeedCount,
    EquivalenceMetricDefinition[] MetricDefinitions,
    EquivalenceBand[] Bands);

internal sealed record EquivalenceBandSet(
    int FormatVersion,
    int AnalysisProtocolVersion,
    string CanonicalCaptureSha256,
    string QualificationProtocolSha256,
    string BandPayloadSha256,
    int SeedCount,
    EquivalenceMetricDefinition[] MetricDefinitions,
    EquivalenceBand[] Bands,
    DateTimeOffset CreatedAtUtc)
{
    internal const int CurrentFormatVersion = 1;
    internal const string FileName = "canonical-bands-64.json";
    private static readonly JsonSerializerOptions _serialiserOptions = new() { WriteIndented = true };

    internal static EquivalenceBandSet Create(
        string canonicalCaptureSha256,
        string qualificationProtocolSha256,
        IReadOnlyList<EquivalenceBand> bands)
    {
        EquivalenceBand[] orderedBands = bands
            .OrderBy(band => band.Condition, StringComparer.Ordinal)
            .ThenBy(band => band.Mcs)
            .ThenBy(band => Array.IndexOf(EventEnsembleProtocol.Metrics, band.Metric))
            .ToArray();
        EquivalenceMetricDefinition[] definitions = EventEnsembleProtocol.Metrics
            .Select(metric => new EquivalenceMetricDefinition(metric, MetricDefinition(metric))).ToArray();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow.ToUniversalTime();
        EquivalenceBandPayload payload = new(
            CurrentFormatVersion,
            EventEnsembleProtocol.Version,
            canonicalCaptureSha256,
            qualificationProtocolSha256,
            64,
            definitions,
            orderedBands);
        return new EquivalenceBandSet(payload.FormatVersion, payload.AnalysisProtocolVersion,
            payload.CanonicalCaptureSha256, payload.QualificationProtocolSha256,
            HashPayload(payload), payload.SeedCount, payload.MetricDefinitions, payload.Bands, createdAt);
    }

    internal void Save(string path)
    {
        ValidateIntegrity();
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(this, _serialiserOptions));
    }

    internal static EquivalenceBandSet Load(string path)
    {
        EquivalenceBandSet bands = JsonSerializer.Deserialize<EquivalenceBandSet>(File.ReadAllBytes(path), _serialiserOptions)
            ?? throw new InvalidDataException("The frozen equivalence-band file was JSON null.");
        bands.ValidateIntegrity();
        return bands;
    }

    internal void ValidateIntegrity()
    {
        if (FormatVersion != CurrentFormatVersion) throw new InvalidDataException("Unsupported frozen-band format version.");
        if (AnalysisProtocolVersion != EventEnsembleProtocol.Version) throw new InvalidDataException("Frozen bands use a different analysis protocol version.");
        if (SeedCount != 64) throw new InvalidDataException("Frozen canonical bands must use 64 seeds.");
        EquivalenceMetricDefinition[] expectedDefinitions = EventEnsembleProtocol.Metrics
            .Select(metric => new EquivalenceMetricDefinition(metric, MetricDefinition(metric))).ToArray();
        if (MetricDefinitions is null || !MetricDefinitions.SequenceEqual(expectedDefinitions))
            throw new InvalidDataException("Frozen-band metric schema differs from the qualification protocol.");
        if (Bands is null || Bands.Length != EventEnsembleAnalysis.ExpectedEndpointCount || Bands.Any(band => band is null))
            throw new InvalidDataException("Frozen-band endpoint count is incomplete.");
        HashSet<(string Condition, int Mcs, string Metric)> uniqueEndpoints = [];
        foreach (EquivalenceBand band in Bands)
        {
            if (!EventEnsembleProtocol.Conditions.Contains(band.Condition, StringComparer.Ordinal) ||
                !EventEnsembleProtocol.Checkpoints(band.Condition).Contains(band.Mcs) ||
                !EventEnsembleProtocol.Metrics.Contains(band.Metric, StringComparer.Ordinal) ||
                !uniqueEndpoints.Add((band.Condition, band.Mcs, band.Metric)))
                throw new InvalidDataException("Frozen-band endpoint keys are unknown or duplicated.");
            double expectedHalfWidth = EventEnsembleProtocol.MeanBandStandardDeviations * band.CanonicalSampleStandardDeviation;
            if (band.LowerBound != band.CanonicalMean - expectedHalfWidth ||
                band.UpperBound != band.CanonicalMean + expectedHalfWidth)
                throw new InvalidDataException("Frozen-band bounds do not match the declared practical-width rule.");
        }
        if (Bands.Any(band => !double.IsFinite(band.CanonicalMean) ||
                              !double.IsFinite(band.CanonicalSampleStandardDeviation) || band.CanonicalSampleStandardDeviation < 0 ||
                              band.SeedCount != SeedCount || !double.IsFinite(band.LowerBound) || !double.IsFinite(band.UpperBound) ||
                              band.LowerBound > band.CanonicalMean || band.UpperBound < band.CanonicalMean))
            throw new InvalidDataException("Frozen-band payload contains invalid values.");
        if (CreatedAtUtc == default || CreatedAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidDataException("Frozen-band creation time must use UTC.");
        if (Convert.FromHexString(CanonicalCaptureSha256).Length != 32 ||
            Convert.FromHexString(QualificationProtocolSha256).Length != 32)
            throw new InvalidDataException("Frozen-band provenance hashes must be SHA-256 values.");
        EquivalenceBandPayload payload = new(FormatVersion, AnalysisProtocolVersion, CanonicalCaptureSha256,
            QualificationProtocolSha256, SeedCount, MetricDefinitions, Bands);
        if (!StringComparer.Ordinal.Equals(BandPayloadSha256, HashPayload(payload)))
            throw new InvalidDataException("Frozen-band payload hash does not match its content.");
    }

    private static string HashPayload(EquivalenceBandPayload payload)
    {
        EquivalenceBand[] orderedBands = payload.Bands
            .OrderBy(band => band.Condition, StringComparer.Ordinal)
            .ThenBy(band => band.Mcs)
            .ThenBy(band => Array.IndexOf(EventEnsembleProtocol.Metrics, band.Metric))
            .ToArray();
        EquivalenceBandPayload orderedPayload = payload with { Bands = orderedBands };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(orderedPayload)));
    }

    private static string MetricDefinition(string metric) => metric switch
    {
        "mixing" => "Heterotypic interface fraction of total cell-cell interface.",
        "heterotypic" => "Count of heterotypic cell-cell interfaces.",
        "total_interface" => "Count of all cell-cell interfaces.",
        "homotypic_a" => "Count of homotypic interfaces for cell type A.",
        "homotypic_b" => "Count of homotypic interfaces for cell type B.",
        "domains_a" => "Number of connected domains for cell type A.",
        "domains_b" => "Number of connected domains for cell type B.",
        "largest_domain_a" => "Cell count in the largest type A domain.",
        "largest_domain_b" => "Cell count in the largest type B domain.",
        "area_mean" => "Mean area across cells in one tissue.",
        "area_sd" => "Population standard deviation of cell area within one tissue.",
        "area_q10" => "Tenth percentile of cell area within one tissue.",
        "area_q50" => "Median cell area within one tissue.",
        "area_q90" => "Ninetieth percentile of cell area within one tissue.",
        "perimeter_mean" => "Mean perimeter across cells in one tissue.",
        "perimeter_sd" => "Population standard deviation of cell perimeter within one tissue.",
        "perimeter_q10" => "Tenth percentile of cell perimeter within one tissue.",
        "perimeter_q50" => "Median cell perimeter within one tissue.",
        "perimeter_q90" => "Ninetieth percentile of cell perimeter within one tissue.",
        "shape_mean" => "Mean cell area divided by squared perimeter within one tissue.",
        "occupied_fraction" => "Fraction of lattice sites occupied by biological cells.",
        "acceptance_fraction" => "Accepted copies divided by canonical-equivalent proposal slots.",
        _ => throw new ArgumentOutOfRangeException(nameof(metric))
    };
}
