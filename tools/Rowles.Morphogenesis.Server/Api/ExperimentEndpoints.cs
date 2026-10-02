using Rowles.Morphogenesis.Server.Experiments;

namespace Rowles.Morphogenesis.Server.Api;

public static class ExperimentEndpoints
{
    public static IEndpointRouteBuilder MapExperimentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/experiments", (ExperimentCatalog catalog) => Results.Ok(
            catalog.Entries.Select(entry => new ExperimentSummaryDto(
                entry.ExperimentId,
                entry.Name,
                entry.GridWidth,
                entry.GridHeight,
                entry.McsCount,
                entry.ReplicateCount,
                entry.BoundaryMode,
                entry.CellTypeNames)).ToArray()))
            .WithName("ListExperiments");

        endpoints.MapGet("/api/experiments/{id}", (string id, ExperimentCatalog catalog) =>
            catalog.TryGet(id, out ExperimentCatalogEntry entry)
                ? Results.Ok(catalog.GetManifest(entry))
                : Results.NotFound())
            .WithName("GetExperiment");

        return endpoints;
    }
}

public sealed record ExperimentSummaryDto(
    string ExperimentId,
    string Name,
    int GridWidth,
    int GridHeight,
    int McsCount,
    int ReplicateCount,
    string BoundaryMode,
    IReadOnlyList<string> CellTypeNames);
