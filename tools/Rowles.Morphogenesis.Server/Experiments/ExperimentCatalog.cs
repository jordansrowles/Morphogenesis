using System.Collections.Immutable;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Server.Configuration;

namespace Rowles.Morphogenesis.Server.Experiments;

public sealed class ExperimentCatalog
{
    private readonly ImmutableArray<ExperimentCatalogEntry> _entries;
    private readonly Dictionary<string, ExperimentCatalogEntry> _byId;

    public ExperimentCatalog(LaboratoryServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string directory = options.CanonicalExperimentsDirectory;
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Canonical experiment directory was not found: {directory}");

        List<ExperimentCatalogEntry> entries = [];
        HashSet<string> identifiers = new(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            FileInfo file = new(path);
            if (file.Length > options.MaxManifestBytes)
                throw new InvalidDataException($"Canonical manifest '{file.Name}' exceeds the configured manifest size limit.");
            string json = File.ReadAllText(path);
            ExperimentManifest manifest;
            try
            {
                manifest = ExperimentManifest.FromJson(json);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or System.Text.Json.JsonException)
            {
                throw new InvalidDataException($"Canonical manifest '{file.Name}' is invalid.", exception);
            }

            if (!identifiers.Add(manifest.ExperimentId))
                throw new InvalidDataException($"Duplicate canonical experiment ID '{manifest.ExperimentId}'.");

            entries.Add(new ExperimentCatalogEntry(
                manifest.ExperimentId,
                manifest.Name,
                manifest.GridWidth,
                manifest.GridHeight,
                manifest.McsCount,
                manifest.ReplicateCount,
                manifest.BoundaryMode.ToString(),
                manifest.CellTypes.Select(cellType => cellType.Name).ToImmutableArray(),
                manifest.ToJson()));
        }

        if (entries.Count == 0)
            throw new InvalidDataException("The canonical experiment catalogue is empty.");

        _entries = entries.OrderBy(entry => entry.ExperimentId, StringComparer.Ordinal).ToImmutableArray();
        _byId = _entries.ToDictionary(entry => entry.ExperimentId, StringComparer.Ordinal);
    }

    public ImmutableArray<ExperimentCatalogEntry> Entries => _entries;

    public bool TryGet(string experimentId, out ExperimentCatalogEntry entry) =>
        _byId.TryGetValue(experimentId, out entry!);

    public ExperimentManifest GetManifest(ExperimentCatalogEntry entry) =>
        ExperimentManifest.FromJson(entry.ManifestJson);
}
