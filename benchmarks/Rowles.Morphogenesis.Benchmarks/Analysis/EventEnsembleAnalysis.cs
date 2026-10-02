using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rowles.Morphogenesis.Benchmarks.Analysis;

internal static class EventEnsembleAnalysis
{
    internal const int ExpectedEndpointCount = 39 * 22;
    internal const int ExpectedFinalVarianceMetricCount = 8 * 22;
    private const string ProtocolCopyName = "qualification-protocol.md";
    private static readonly string[] _rowIdentityFields = ["initialisation_seed", "dynamics_seed", "initial_hash"];

    internal static EquivalenceBandSet Freeze(string directory, string protocolPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolPath);
        string root = Path.GetFullPath(directory);
        if (File.Exists(Path.Combine(root, EquivalenceBandSet.FileName)))
            throw new InvalidOperationException("Frozen canonical bands already exist; they cannot be replaced.");
        if (new[] { "border", "directed" }.Any(kernel => new[] { 64, 128, 256 }
            .Any(seeds => File.Exists(Path.Combine(root, $"{kernel}-samples-{seeds}.csv")))))
            throw new InvalidOperationException("Freeze the canonical bands before accelerated captures are present.");

        Directory.CreateDirectory(root);
        CaptureData canonical = ReadCapture(root, "canonical", 64);
        string canonicalIdentity = CaptureIdentity(root, "canonical", 64);
        byte[] protocolBytes = File.ReadAllBytes(protocolPath);
        string protocolHash = Hash(protocolBytes);
        string protocolCopy = Path.Combine(root, ProtocolCopyName);
        if (File.Exists(protocolCopy))
        {
            if (Hash(File.ReadAllBytes(protocolCopy)) != protocolHash)
                throw new InvalidOperationException("The qualification protocol copy already differs from the supplied protocol file.");
        }
        else
        {
            File.WriteAllBytes(protocolCopy, protocolBytes);
        }

        List<EquivalenceBand> bands = new(ExpectedEndpointCount);
        foreach ((EndpointKey endpoint, CaptureRow[] rows) in canonical.Rows.OrderBy(pair => pair.Key))
        {
            for (int metricIndex = 0; metricIndex < EventEnsembleProtocol.Metrics.Length; metricIndex++)
            {
                double[] values = rows.Select(row => row.Metrics[metricIndex]).ToArray();
                double mean = PairedInterval.Mean(values);
                double standardDeviation = PairedInterval.SampleStandardDeviation(values);
                double halfWidth = EventEnsembleProtocol.MeanBandStandardDeviations * standardDeviation;
                bands.Add(new EquivalenceBand(endpoint.Condition, endpoint.Mcs,
                    EventEnsembleProtocol.Metrics[metricIndex], 64, mean, standardDeviation,
                    mean - halfWidth, mean + halfWidth));
            }
        }

        EquivalenceBandSet set = EquivalenceBandSet.Create(canonicalIdentity, protocolHash, bands);
        set.Save(Path.Combine(root, EquivalenceBandSet.FileName));
        return set;
    }

    internal static EnsembleQualificationResult Compare(string kernel, string directory, int seedCount)
    {
        if (kernel is not ("border" or "directed"))
            throw new ArgumentException("Kernel must be 'border' or 'directed'.", nameof(kernel));
        if (seedCount is not (64 or 128 or 256))
            throw new ArgumentOutOfRangeException(nameof(seedCount), "Seed count must be 64, 128, or 256.");

        string root = Path.GetFullPath(directory);
        List<string> provenanceFailures = [];
        EquivalenceBandSet? bands = null;
        string bandPath = Path.Combine(root, EquivalenceBandSet.FileName);
        try
        {
            bands = EquivalenceBandSet.Load(bandPath);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or FormatException or ArgumentException)
        {
            provenanceFailures.Add($"Frozen-band provenance is invalid: {exception.Message}");
        }

        if (bands is not null)
        {
            ValidateProtocolCopy(root, bands, provenanceFailures);
            ValidateCaptureIdentity(root, bands, provenanceFailures);
            ValidateManifestIdentities(root, kernel, seedCount, provenanceFailures);
            if (bands.SeedCount != 64 || bands.AnalysisProtocolVersion != EventEnsembleProtocol.Version)
                provenanceFailures.Add("Frozen-band seed count or analysis protocol version differs from this qualification.");
            if (!bands.MetricDefinitions.Select(metric => metric.Name).SequenceEqual(EventEnsembleProtocol.Metrics))
                provenanceFailures.Add("Frozen-band metric schema differs from this qualification.");
        }

        CaptureData canonical = ReadCapture(root, "canonical", seedCount);
        CaptureData candidate = ReadCapture(root, kernel, seedCount);
        if (bands is not null) ValidateBandsAgainstCanonical(bands, canonical, provenanceFailures);
        ValidateCanonicalPrefix(root, canonical, seedCount, provenanceFailures);
        ValidatePairedIdentity(canonical, candidate, provenanceFailures);

        if (provenanceFailures.Count > 0)
        {
            return new EnsembleQualificationResult(kernel, seedCount, 0, 0, 0, 0,
                provenanceFailures.Count, 0, false, provenanceFailures.ToArray());
        }

        Dictionary<(EndpointKey Endpoint, string Metric), EquivalenceBand> bandMap = bands?.Bands
            .ToDictionary(band => (new EndpointKey(band.Condition, band.Mcs), band.Metric)) ?? [];
        List<string> failures = [.. provenanceFailures];
        int checkpointZeroFailures = 0;
        int meanFailures = 0;
        int varianceFailures = 0;
        int evaluated = 0;
        int varianceEvaluated = 0;
        string comparisonPath = Path.Combine(root, $"{kernel}-comparison-{seedCount}.csv");
        using StreamWriter comparison = new(comparisonPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        WriteCsvRow(comparison, ["condition", "mcs", "metric", "seeds", "canonical_mean", "candidate_mean",
            "difference", "difference_ci_low", "difference_ci_high", "frozen_half_width", "effect_in_canonical_sd",
            "mean_equivalent", "variance_ratio", "variance_ci_low", "variance_ci_high", "variance_equivalent"]);

        foreach ((EndpointKey endpoint, CaptureRow[] canonicalRows) in canonical.Rows.OrderBy(pair => pair.Key))
        {
            CaptureRow[] candidateRows = candidate.Rows[endpoint];
            for (int metricIndex = 0; metricIndex < EventEnsembleProtocol.Metrics.Length; metricIndex++)
            {
                string metric = EventEnsembleProtocol.Metrics[metricIndex];
                double[] canonicalValues = canonicalRows.Select(row => row.Metrics[metricIndex]).ToArray();
                double[] candidateValues = candidateRows.Select(row => row.Metrics[metricIndex]).ToArray();
                if (!bandMap.TryGetValue((endpoint, metric), out EquivalenceBand? band))
                {
                    failures.Add($"Frozen practical band is missing for {endpoint.Condition}/{endpoint.Mcs}/{metric}.");
                    provenanceFailures.Add($"Frozen practical band is missing for {endpoint.Condition}/{endpoint.Mcs}/{metric}.");
                    continue;
                }

                if (endpoint.Mcs == 0 && !canonicalValues.AsSpan().SequenceEqual(candidateValues))
                {
                    checkpointZeroFailures++;
                    failures.Add($"Checkpoint zero differs for {endpoint.Condition}/{metric}.");
                }

                double halfWidth = EventEnsembleProtocol.MeanBandStandardDeviations * band.CanonicalSampleStandardDeviation;
                PairedIntervalResult interval = PairedInterval.Calculate(canonicalValues, candidateValues, halfWidth);
                bool meanEquivalent = interval.IsEquivalent;
                double effect = band.CanonicalSampleStandardDeviation == 0
                    ? interval.MeanDifference == 0 ? 0 : Math.CopySign(double.PositiveInfinity, interval.MeanDifference)
                    : interval.MeanDifference / band.CanonicalSampleStandardDeviation;
                string varianceRatio = "";
                string varianceLow = "";
                string varianceHigh = "";
                string varianceEquivalent = "";
                bool isFinalCheckpoint = endpoint.Mcs == EventEnsembleProtocol.Checkpoints(endpoint.Condition)[^1];
                if (endpoint.Mcs > 0)
                {
                    evaluated++;
                    if (!meanEquivalent)
                    {
                        meanFailures++;
                        failures.Add($"Mean interval outside frozen band for {endpoint.Condition}/{endpoint.Mcs}/{metric}: [{interval.LowerBound:R}, {interval.UpperBound:R}] vs ±{halfWidth:R}.");
                    }
                }

                if (isFinalCheckpoint)
                {
                    varianceEvaluated++;
                    BootstrapVarianceResult variance = BootstrapVariance.Calculate(canonicalValues, candidateValues,
                        EventEnsembleProtocol.BootstrapKey(endpoint.Condition, metric, kernel, seedCount));
                    varianceRatio = Format(variance.Ratio);
                    varianceLow = Format(variance.LowerBound);
                    varianceHigh = Format(variance.UpperBound);
                    varianceEquivalent = variance.IsEquivalent.ToString(CultureInfo.InvariantCulture);
                    if (!variance.IsEquivalent)
                    {
                        varianceFailures++;
                        failures.Add($"Final variance interval outside bounds for {endpoint.Condition}/{metric}: [{variance.LowerBound:R}, {variance.UpperBound:R}].");
                    }
                }

                WriteCsvRow(comparison, [endpoint.Condition, endpoint.Mcs.ToString(CultureInfo.InvariantCulture), metric,
                    seedCount.ToString(CultureInfo.InvariantCulture), Format(PairedInterval.Mean(canonicalValues)),
                    Format(PairedInterval.Mean(candidateValues)), Format(interval.MeanDifference), Format(interval.LowerBound),
                    Format(interval.UpperBound), Format(halfWidth), Format(effect), meanEquivalent.ToString(CultureInfo.InvariantCulture),
                    varianceRatio, varianceLow, varianceHigh, varianceEquivalent]);
            }
        }

        int finalProvenanceFailures = provenanceFailures.Count;
        return new EnsembleQualificationResult(kernel, seedCount, evaluated, varianceEvaluated, meanFailures, varianceFailures,
            finalProvenanceFailures, checkpointZeroFailures,
            meanFailures == 0 && varianceFailures == 0 && finalProvenanceFailures == 0 && checkpointZeroFailures == 0,
            failures.ToArray());
    }

    internal static string ConfigurationIdentity(string manifestPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Resolved ensemble manifest must be a JSON object.");
        if (!root.TryGetProperty("configuration", out JsonElement configuration))
            throw new InvalidDataException("Resolved ensemble manifest is missing its simulation configuration.");
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            WriteCanonicalJson(writer, configuration);
        }
        return Hash(stream.ToArray());
    }

    private static CaptureData ReadCapture(string root, string kernel, int seedCount)
    {
        string path = Path.Combine(root, $"{kernel}-samples-{seedCount}.csv");
        if (!File.Exists(path)) throw new FileNotFoundException($"Required {kernel} {seedCount}-seed capture is missing.", path);
        using StreamReader reader = new(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? headerLine = reader.ReadLine();
        if (headerLine is null) throw new InvalidDataException($"Capture '{path}' is empty.");
        string[] headers = ParseCsvRow(headerLine).ToArray();
        if (headers.Distinct(StringComparer.Ordinal).Count() != headers.Length)
            throw new InvalidDataException($"Capture '{path}' has duplicate column names.");
        Dictionary<string, int> positions = headers.Select((name, index) => (name, index))
            .ToDictionary(item => item.name, item => item.index, StringComparer.Ordinal);
        string[] required = ["condition", "kernel", "replicate", "initialisation_seed", "dynamics_seed", "initial_hash", "mcs", .. EventEnsembleProtocol.Metrics];
        foreach (string name in required)
            if (!positions.ContainsKey(name)) throw new InvalidDataException($"Capture '{path}' is missing required metric or identity column '{name}'.");

        Dictionary<EndpointKey, CaptureRow[]> groups = [];
        string? line;
        int lineNumber = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            string[] fields = ParseCsvRow(line).ToArray();
            if (fields.Length != headers.Length) throw new InvalidDataException($"Capture '{path}' row {lineNumber} has {fields.Length} fields; expected {headers.Length}.");
            string condition = fields[positions["condition"]];
            string rowKernel = fields[positions["kernel"]];
            if (!EventEnsembleProtocol.Conditions.Contains(condition, StringComparer.Ordinal))
                throw new InvalidDataException($"Capture '{path}' row {lineNumber} has unknown condition '{condition}'.");
            if (rowKernel != kernel) throw new InvalidDataException($"Capture '{path}' row {lineNumber} identifies kernel '{rowKernel}', expected '{kernel}'.");
            if (!int.TryParse(fields[positions["replicate"]], NumberStyles.None, CultureInfo.InvariantCulture, out int replicate) || replicate < 0 || replicate >= seedCount)
                throw new InvalidDataException($"Capture '{path}' row {lineNumber} has an invalid replicate index.");
            if (!int.TryParse(fields[positions["mcs"]], NumberStyles.None, CultureInfo.InvariantCulture, out int mcs) ||
                !EventEnsembleProtocol.Checkpoints(condition).Contains(mcs))
                throw new InvalidDataException($"Capture '{path}' row {lineNumber} has an unexpected checkpoint.");
            if (!ulong.TryParse(fields[positions["initialisation_seed"]], NumberStyles.None, CultureInfo.InvariantCulture, out ulong initialisationSeed) ||
                !ulong.TryParse(fields[positions["dynamics_seed"]], NumberStyles.None, CultureInfo.InvariantCulture, out ulong dynamicsSeed))
                throw new InvalidDataException($"Capture '{path}' row {lineNumber} has an invalid paired seed.");
            string initialHash = fields[positions["initial_hash"]];
            if (initialHash.Length != 16 || !ulong.TryParse(initialHash, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                throw new InvalidDataException($"Capture '{path}' row {lineNumber} has an invalid initial-state hash.");

            double[] values = new double[EventEnsembleProtocol.Metrics.Length];
            for (int metric = 0; metric < values.Length; metric++)
            {
                if (!double.TryParse(fields[positions[EventEnsembleProtocol.Metrics[metric]]], NumberStyles.Float, CultureInfo.InvariantCulture, out values[metric]) || !double.IsFinite(values[metric]))
                    throw new InvalidDataException($"Capture '{path}' row {lineNumber} has an invalid value for '{EventEnsembleProtocol.Metrics[metric]}'.");
            }

            EndpointKey key = new(condition, mcs);
            if (!groups.TryGetValue(key, out CaptureRow[]? rows))
            {
                rows = new CaptureRow[seedCount];
                groups.Add(key, rows);
            }
            if (rows[replicate] is not null) throw new InvalidDataException($"Capture '{path}' duplicates seed {replicate} for {condition}/{mcs}.");
            rows[replicate] = new CaptureRow(replicate, initialisationSeed, dynamicsSeed, initialHash, values, fields);
        }

        if (groups.Count != ExpectedEndpointCount / EventEnsembleProtocol.Metrics.Length)
            throw new InvalidDataException($"Capture '{path}' contains {groups.Count} condition/checkpoint groups; expected 39.");
        foreach (string condition in EventEnsembleProtocol.Conditions)
        {
            CaptureRow[] initialRows = groups[new EndpointKey(condition, 0)];
            foreach (int checkpoint in EventEnsembleProtocol.Checkpoints(condition))
            {
                EndpointKey key = new(condition, checkpoint);
                if (!groups.TryGetValue(key, out CaptureRow[]? rows) || rows.Any(row => row is null))
                    throw new InvalidDataException($"Capture '{path}' is missing one or more paired seeds for {condition}/{checkpoint}.");
                for (int replicate = 0; replicate < seedCount; replicate++)
                {
                    CaptureRow initial = initialRows[replicate];
                    CaptureRow current = rows[replicate];
                    if (initial.InitialisationSeed != current.InitialisationSeed ||
                        initial.DynamicsSeed != current.DynamicsSeed ||
                        initial.InitialHash != current.InitialHash)
                        throw new InvalidDataException($"Capture '{path}' changes paired seed or initial tissue across checkpoints for {condition}/seed {replicate}.");
                }
            }
        }
        return new CaptureData(groups);
    }

    private static void ValidateProtocolCopy(string root, EquivalenceBandSet bands, List<string> failures)
    {
        string path = Path.Combine(root, ProtocolCopyName);
        if (!File.Exists(path))
        {
            failures.Add("Frozen qualification protocol copy is missing.");
            return;
        }
        if (Hash(File.ReadAllBytes(path)) != bands.QualificationProtocolSha256)
            failures.Add("Qualification protocol file SHA-256 differs from frozen provenance.");
    }

    private static void ValidateCaptureIdentity(string root, EquivalenceBandSet bands, List<string> failures)
    {
        try
        {
            string actual = CaptureIdentity(root, "canonical", 64);
            if (actual != bands.CanonicalCaptureSha256)
                failures.Add("Canonical capture SHA-256 differs from frozen provenance.");
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or FormatException)
        {
            failures.Add($"Canonical capture provenance cannot be verified: {exception.Message}");
        }
    }

    private static void ValidateManifestIdentities(string root, string kernel, int seedCount, List<string> failures)
    {
        foreach (string condition in EventEnsembleProtocol.Conditions)
        {
            string canonicalPath = ManifestPath(root, condition, "canonical", seedCount);
            if (!File.Exists(canonicalPath))
            {
                failures.Add($"Canonical resolved manifest is missing for {condition}/{seedCount}.");
                continue;
            }
            ValidateResolvedManifestKernel(canonicalPath, "canonical", condition, seedCount, failures);
            string expected;
            try { expected = ConfigurationIdentity(canonicalPath); }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
            {
                failures.Add($"Canonical manifest is invalid for {condition}/{seedCount}: {exception.Message}");
                continue;
            }

            string path = ManifestPath(root, condition, kernel, seedCount);
            if (!File.Exists(path))
            {
                failures.Add($"{kernel} resolved manifest is missing for {condition}/{seedCount}.");
                continue;
            }
            ValidateResolvedManifestKernel(path, kernel, condition, seedCount, failures);
            try
            {
                if (ConfigurationIdentity(path) != expected)
                    failures.Add($"Resolved simulation configuration differs for {condition}/{seedCount}/{kernel}.");
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
            {
                failures.Add($"{kernel} manifest is invalid for {condition}/{seedCount}: {exception.Message}");
            }
        }
    }

    private static void ValidateResolvedManifestKernel(string path, string expectedKernel, string condition,
        int seedCount, List<string> failures)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            if (!document.RootElement.TryGetProperty("kernel", out JsonElement kernel) ||
                kernel.ValueKind != JsonValueKind.String || kernel.GetString() != expectedKernel)
                failures.Add($"Resolved manifest kernel is incorrect for {condition}/{seedCount}; expected '{expectedKernel}'.");
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            failures.Add($"Resolved manifest is invalid for {condition}/{seedCount}: {exception.Message}");
        }
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement value in element.EnumerateArray()) WriteCanonicalJson(writer, value);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static void ValidateCanonicalPrefix(string root, CaptureData canonical, int seedCount, List<string> failures)
    {
        if (seedCount == 64) return;
        try
        {
            CaptureData prefix = ReadCapture(root, "canonical", 64);
            foreach ((EndpointKey key, CaptureRow[] rows) in prefix.Rows)
            {
                CaptureRow[] expandedRows = canonical.Rows[key];
                for (int replicate = 0; replicate < 64; replicate++)
                {
                    if (!rows[replicate].RawFields.SequenceEqual(expandedRows[replicate].RawFields, StringComparer.Ordinal))
                    {
                        failures.Add($"Canonical seed prefix changed for {key.Condition}/{key.Mcs}/seed {replicate}.");
                        return;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            failures.Add($"Canonical seed prefix cannot be verified: {exception.Message}");
        }
    }

    private static void ValidatePairedIdentity(CaptureData canonical, CaptureData candidate, List<string> failures)
    {
        foreach ((EndpointKey key, CaptureRow[] rows) in canonical.Rows)
        {
            CaptureRow[] candidates = candidate.Rows[key];
            for (int replicate = 0; replicate < rows.Length; replicate++)
            {
                if (rows[replicate].InitialisationSeed != candidates[replicate].InitialisationSeed ||
                    rows[replicate].DynamicsSeed != candidates[replicate].DynamicsSeed ||
                    rows[replicate].InitialHash != candidates[replicate].InitialHash)
                {
                    failures.Add($"Paired seeds or initial tissue differ for {key.Condition}/{key.Mcs}/seed {replicate}.");
                    return;
                }
            }
        }
    }

    private static string CaptureIdentity(string root, string kernel, int seedCount)
    {
        using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        string samplesName = $"{kernel}-samples-{seedCount}.csv";
        AppendIdentityFile(root, samplesName, digest);
        foreach (string condition in EventEnsembleProtocol.Conditions.Order(StringComparer.Ordinal))
            AppendIdentityFile(root, Path.GetFileName(ManifestPath(root, condition, kernel, seedCount)), digest);
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    private static void AppendIdentityFile(string root, string name, IncrementalHash digest)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        digest.AppendData(nameBytes);
        digest.AppendData([0]);
        string path = Path.Combine(root, name);
        using FileStream stream = File.OpenRead(path);
        byte[] buffer = new byte[65536];
        int read;
        while ((read = stream.Read(buffer)) > 0) digest.AppendData(buffer, 0, read);
        digest.AppendData([0]);
    }

    private static string ManifestPath(string root, string condition, string kernel, int seedCount) =>
        Path.Combine(root, $"{condition}-{kernel}-manifest-{seedCount}.json");

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static void WriteCsvRow(StreamWriter writer, IReadOnlyList<string> fields)
    {
        for (int index = 0; index < fields.Count; index++)
        {
            if (index > 0) writer.Write(',');
            string field = fields[index];
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
                writer.Write('"' + field.Replace("\"", "\"\"", StringComparison.Ordinal) + '"');
            else writer.Write(field);
        }
        writer.WriteLine();
    }

    private static void ValidateBandsAgainstCanonical(EquivalenceBandSet bands, CaptureData canonical, List<string> failures)
    {
        Dictionary<(string Condition, int Mcs, string Metric), EquivalenceBand> bandMap = bands.Bands
            .ToDictionary(band => (band.Condition, band.Mcs, band.Metric));
        foreach ((EndpointKey endpoint, CaptureRow[] allRows) in canonical.Rows)
        {
            CaptureRow[] rows = allRows.Take(64).ToArray();
            for (int metricIndex = 0; metricIndex < EventEnsembleProtocol.Metrics.Length; metricIndex++)
            {
                string metric = EventEnsembleProtocol.Metrics[metricIndex];
                if (!bandMap.TryGetValue((endpoint.Condition, endpoint.Mcs, metric), out EquivalenceBand? band))
                {
                    failures.Add($"Frozen canonical band is missing for {endpoint.Condition}/{endpoint.Mcs}/{metric}.");
                    continue;
                }
                double[] values = rows.Select(row => row.Metrics[metricIndex]).ToArray();
                double mean = PairedInterval.Mean(values);
                double standardDeviation = PairedInterval.SampleStandardDeviation(values);
                double halfWidth = EventEnsembleProtocol.MeanBandStandardDeviations * standardDeviation;
                if (band.SeedCount != 64 || band.CanonicalMean != mean ||
                    band.CanonicalSampleStandardDeviation != standardDeviation ||
                    band.LowerBound != mean - halfWidth || band.UpperBound != mean + halfWidth)
                {
                    failures.Add($"Frozen canonical band does not match the paired 64-seed capture for {endpoint.Condition}/{endpoint.Mcs}/{metric}.");
                    return;
                }
            }
        }
    }

    private static IEnumerable<string> ParseCsvRow(string line)
    {
        StringBuilder field = new();
        bool quoted = false;
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"') quoted = false;
                else field.Append(character);
            }
            else if (character == '"' && field.Length == 0) quoted = true;
            else if (character == ',')
            {
                yield return field.ToString();
                field.Clear();
            }
            else field.Append(character);
        }
        if (quoted) throw new InvalidDataException("CSV field has an unterminated quote.");
        yield return field.ToString();
    }

    private readonly record struct EndpointKey(string Condition, int Mcs) : IComparable<EndpointKey>
    {
        public int CompareTo(EndpointKey other)
        {
            int condition = StringComparer.Ordinal.Compare(Condition, other.Condition);
            return condition != 0 ? condition : Mcs.CompareTo(other.Mcs);
        }
    }

    private sealed record CaptureRow(int Replicate, ulong InitialisationSeed, ulong DynamicsSeed, string InitialHash, double[] Metrics, string[] RawFields);
    private sealed record CaptureData(Dictionary<EndpointKey, CaptureRow[]> Rows);
}
