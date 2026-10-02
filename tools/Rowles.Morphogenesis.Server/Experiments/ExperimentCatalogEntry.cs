using System.Collections.Immutable;

namespace Rowles.Morphogenesis.Server.Experiments;

public sealed record ExperimentCatalogEntry(
    string ExperimentId,
    string Name,
    int GridWidth,
    int GridHeight,
    int McsCount,
    int ReplicateCount,
    string BoundaryMode,
    ImmutableArray<string> CellTypeNames,
    string ManifestJson);
