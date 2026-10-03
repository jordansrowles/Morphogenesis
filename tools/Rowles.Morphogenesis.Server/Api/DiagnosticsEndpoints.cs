using System.Reflection;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Server.Diagnostics;
using Rowles.Morphogenesis.Server.Persistence;
using Rowles.Morphogenesis.Server.Sessions;

namespace Rowles.Morphogenesis.Server.Api;

public static class DiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", async (LaboratoryDatabase database, CancellationToken cancellationToken) =>
        {
            try
            {
                int value = await database.HealthCheckAsync(cancellationToken).ConfigureAwait(false);
                return value == 1
                    ? Results.Ok(new { status = "healthy", database = "available" })
                    : Results.Json(new { status = "unhealthy", database = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidOperationException)
            {
                return Results.Json(new { status = "unhealthy", database = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithName("Health");

        endpoints.MapGet("/api/info", () => Results.Ok(new
        {
            application = "Rowles.Morphogenesis.Server",
            applicationVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0",
            runtimeVersion = Environment.Version.ToString(),
            kernelId = SerialSimulation.KernelId,
            recordingSchemaVersion = RecordingFormat.SchemaVersion,
            databaseSchemaVersion = LaboratoryDatabase.SchemaVersion,
            authentication = "none",
            trustedNetworkOnly = true
        })).WithName("ApplicationInfo");

        endpoints.MapGet("/api/diagnostics", (
            LaboratoryDiagnostics diagnostics,
            SimulationSessionRegistry registry,
            SqliteWriteQueue writeQueue) => Results.Ok(
                diagnostics.Capture(registry.GetLiveSessions(), writeQueue.QueueDepth)))
            .WithName("LaboratoryDiagnostics");

        return endpoints;
    }
}
