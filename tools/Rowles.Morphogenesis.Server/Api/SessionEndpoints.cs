using Rowles.Morphogenesis.Laboratory.Playback;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Server.Persistence;
using Rowles.Morphogenesis.Server.Sessions;
using Serilog.Context;

namespace Rowles.Morphogenesis.Server.Api;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder sessions = endpoints.MapGroup("/api/sessions");
        sessions.MapPost("", CreateSessionAsync).WithName("CreateSession");
        sessions.MapGet("", ListSessionsAsync).WithName("ListSessions");
        sessions.MapGet("/{id:guid}", GetSessionAsync).WithName("GetSession");
        sessions.MapPost("/{id:guid}/start", StartAsync).WithName("StartSession");
        sessions.MapPost("/{id:guid}/pause", PauseAsync).WithName("PauseSession");
        sessions.MapPost("/{id:guid}/resume", ResumeAsync).WithName("ResumeSession");
        sessions.MapPost("/{id:guid}/step", StepAsync).WithName("StepSession");
        sessions.MapPost("/{id:guid}/stop", StopAsync).WithName("StopSession");
        sessions.MapGet("/{id:guid}/metrics", GetMetricsAsync).WithName("GetSessionMetrics");
        sessions.MapGet("/{id:guid}/recording", GetRecordingAsync).WithName("GetSessionRecording");
        sessions.MapGet("/{id:guid}/result", GetResultAsync).WithName("GetSessionResult");
        sessions.MapGet("/{id:guid}/cells/{cellId:int}", InspectCellAsync).WithName("InspectCell");
        return endpoints;
    }

    private static async Task<IResult> CreateSessionAsync(
        CreateSessionRequest? request,
        SimulationSessionRegistry registry,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ILogger logger = loggerFactory.CreateLogger("Rowles.Morphogenesis.Server.SessionEndpoints");
        if (request is null)
            return Results.BadRequest(new { error = "A session request body is required." });
        try
        {
            using IDisposable experimentContext = LogContext.PushProperty("ExperimentId", request.ExperimentId);
            SessionDto session = await registry.CreateSessionAsync(request, cancellationToken).ConfigureAwait(false);
            using IDisposable sessionContext = LogContext.PushProperty("SessionId", session.SessionId.ToString("D"));
            using IDisposable replicateContext = LogContext.PushProperty("ReplicateId", session.ReplicateId);
            using IDisposable mcsContext = LogContext.PushProperty("Mcs", session.CurrentMcs);
            using IDisposable revisionContext = LogContext.PushProperty("Revision", session.Revision);
            logger.LogInformation("Created simulation session {SessionId}", session.SessionId);
            return Results.Created($"/api/sessions/{session.SessionId:D}", session);
        }
        catch (SessionRequestException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or ArgumentException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> ListSessionsAsync(
        int? take,
        SimulationSessionRegistry registry,
        CancellationToken cancellationToken)
    {
        int count = take ?? 100;
        if (count is < 1 or > 500)
            return Results.BadRequest(new { error = "take must be between 1 and 500." });
        return Results.Ok(await registry.GetSessionsAsync(count, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> GetSessionAsync(
        Guid id,
        SimulationSessionRegistry registry,
        CancellationToken cancellationToken)
    {
        SessionDto? session = await registry.GetSessionAsync(id, cancellationToken).ConfigureAwait(false);
        return session is null ? Results.NotFound() : Results.Ok(session);
    }

    private static Task<IResult> StartAsync(Guid id, SessionCommandRequest? request, SimulationSessionRegistry registry, ILoggerFactory loggerFactory, CancellationToken token) =>
        ExecuteCommandAsync(id, request, registry, loggerFactory, "start", static (session, command, ct) => session.StartAsync(command.CommandId, command.ExpectedRevision, ct), token);

    private static Task<IResult> PauseAsync(Guid id, SessionCommandRequest? request, SimulationSessionRegistry registry, ILoggerFactory loggerFactory, CancellationToken token) =>
        ExecuteCommandAsync(id, request, registry, loggerFactory, "pause", static (session, command, ct) => session.PauseAsync(command.CommandId, command.ExpectedRevision, ct), token);

    private static Task<IResult> ResumeAsync(Guid id, SessionCommandRequest? request, SimulationSessionRegistry registry, ILoggerFactory loggerFactory, CancellationToken token) =>
        ExecuteCommandAsync(id, request, registry, loggerFactory, "resume", static (session, command, ct) => session.ResumeAsync(command.CommandId, command.ExpectedRevision, ct), token);

    private static Task<IResult> StepAsync(Guid id, SessionCommandRequest? request, SimulationSessionRegistry registry, ILoggerFactory loggerFactory, CancellationToken token) =>
        ExecuteCommandAsync(id, request, registry, loggerFactory, "step", static (session, command, ct) => session.StepAsync(command.CommandId, command.ExpectedRevision, ct), token);

    private static Task<IResult> StopAsync(Guid id, SessionCommandRequest? request, SimulationSessionRegistry registry, ILoggerFactory loggerFactory, CancellationToken token) =>
        ExecuteCommandAsync(id, request, registry, loggerFactory, "stop", static (session, command, ct) => session.StopAsync(command.CommandId, command.ExpectedRevision, ct), token);

    private static async Task<IResult> ExecuteCommandAsync(
        Guid id,
        SessionCommandRequest? request,
        SimulationSessionRegistry registry,
        ILoggerFactory loggerFactory,
        string commandName,
        Func<SimulationSession, SessionCommandRequest, CancellationToken, ValueTask<SimulationCommandResult>> command,
        CancellationToken cancellationToken)
    {
        ILogger logger = loggerFactory.CreateLogger("Rowles.Morphogenesis.Server.SessionEndpoints");
        if (request is null || request.CommandId == Guid.Empty || request.ExpectedRevision < 0)
            return Results.BadRequest(new { error = "commandId and a non-negative expectedRevision are required." });
        using IDisposable sessionContext = LogContext.PushProperty("SessionId", id.ToString("D"));
        using IDisposable commandContext = LogContext.PushProperty("CommandId", request.CommandId);
        using IDisposable expectedRevisionContext = LogContext.PushProperty("ExpectedRevision", request.ExpectedRevision);
        if (!registry.TryGetSession(id, out SimulationSession session))
        {
            SessionDto? historical = await registry.GetSessionAsync(id, cancellationToken).ConfigureAwait(false);
            if (historical is not null)
            {
                using IDisposable experimentContext = LogContext.PushProperty("ExperimentId", historical.ExperimentId);
                using IDisposable replicateContext = LogContext.PushProperty("ReplicateId", historical.ReplicateId);
                using IDisposable mcsContext = LogContext.PushProperty("Mcs", historical.CurrentMcs);
                using IDisposable revisionContext = LogContext.PushProperty("Revision", historical.Revision);
                logger.LogInformation("Rejected {CommandName} command for a persisted-only session", commandName);
            }
            return historical is null
                ? Results.NotFound()
                : Results.Conflict(historical);
        }

        SimulationCommandResult result = await command(session, request, cancellationToken).ConfigureAwait(false);
        SessionDto? authoritative = await registry.GetAuthoritativeDtoAsync(id, cancellationToken).ConfigureAwait(false);
        if (authoritative is not null)
        {
            using IDisposable experimentContext = LogContext.PushProperty("ExperimentId", authoritative.ExperimentId);
            using IDisposable replicateContext = LogContext.PushProperty("ReplicateId", authoritative.ReplicateId);
            using IDisposable mcsContext = LogContext.PushProperty("Mcs", authoritative.CurrentMcs);
            using IDisposable revisionContext = LogContext.PushProperty("Revision", authoritative.Revision);
            logger.LogInformation("Processed {CommandName} session command as {CommandDisposition}", commandName, result.Disposition);
        }
        return result.Disposition == SimulationCommandDisposition.Applied
            ? Results.Ok(authoritative)
            : Results.Conflict(authoritative);
    }

    private static async Task<IResult> GetMetricsAsync(
        Guid id,
        SimulationSessionRegistry registry,
        CancellationToken cancellationToken)
    {
        if (!await registry.HasRunAsync(id, cancellationToken).ConfigureAwait(false))
            return Results.NotFound();
        return Results.Ok(await registry.GetMetricsAsync(id, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> GetRecordingAsync(
        Guid id,
        SimulationSessionRegistry registry,
        LaboratoryDatabase database,
        SqliteRecordingReader reader,
        CancellationToken cancellationToken)
    {
        PersistedRun? run = await database.GetRunAsync(id, cancellationToken).ConfigureAwait(false);
        if (run is null)
            return Results.NotFound();
        RecordingSettings settings = run.Payload.Recording;
        IReadOnlyList<RecordingFrameIndexEntry> index = settings.Enabled
            ? await reader.GetFrameIndexAsync(id, cancellationToken).ConfigureAwait(false)
            : [];
        SessionDto? session = await registry.GetSessionAsync(id, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new RecordingDto(
            id,
            session?.RecordingState ?? run.RecordingState,
            session?.RecordingFailure ?? run.RecordingFailure,
            RecordingFormat.SchemaVersion,
            settings.RecordEveryMcs,
            settings.KeyframeEveryRecordedFrames,
            settings.Compression,
            index.Count == 0 ? null : index[0].Mcs,
            index.Count == 0 ? null : index[^1].Mcs,
            index.Count,
            index.Select(frame => new RecordingFrameDto(frame.Sequence, frame.Mcs, frame.Kind)).ToArray()));
    }

    private static async Task<IResult> GetResultAsync(
        Guid id,
        SimulationSessionRegistry registry,
        LaboratoryDatabase database,
        CancellationToken cancellationToken)
    {
        if (registry.TryGetSession(id, out SimulationSession session))
        {
            SimulationSessionSnapshot snapshot = session.GetSnapshot();
            if (!IsTerminal(snapshot.Status))
                return Results.NoContent();
            PersistedRun? liveRun = await database.GetRunAsync(id, cancellationToken).ConfigureAwait(false);
            if (liveRun is null)
                return Results.NotFound();
            TerminalRunSummary? storedSummary = liveRun.Payload.Result;
            PersistedMetricSample? finalMeasurement = storedSummary?.FinalMeasurement ??
                (snapshot.LatestMeasurement is null
                    ? await database.GetLatestMetricAsync(id, cancellationToken).ConfigureAwait(false)
                    : MetricRowMapper.ToSample(snapshot.LatestMeasurement));
            TerminalRunSummary summary = storedSummary is null
                ? new TerminalRunSummary(
                snapshot.Status,
                snapshot.CurrentMcs,
                session.Metadata.RunIdentity,
                finalMeasurement,
                snapshot.Failure,
                snapshot.RecordingState,
                snapshot.RecordingFailure)
                : storedSummary with
                {
                    Status = snapshot.Status,
                    FinalMcs = snapshot.CurrentMcs,
                    RunIdentity = session.Metadata.RunIdentity,
                    FinalMeasurement = finalMeasurement,
                    Failure = snapshot.Failure,
                    RecordingState = snapshot.RecordingState,
                    RecordingFailure = snapshot.RecordingFailure
                };
            return Results.Ok(summary);
        }

        PersistedRun? run = await database.GetRunAsync(id, cancellationToken).ConfigureAwait(false);
        if (run is null)
            return Results.NotFound();
        if (!IsTerminal(run.Status))
            return Results.NoContent();
        TerminalRunSummary? persistedSummary = run.Payload.Result;
        if (persistedSummary is not null && persistedSummary.FinalMeasurement is not null)
            return Results.Ok(persistedSummary);
        PersistedMetricSample? latestMeasurement = await database.GetLatestMetricAsync(id, cancellationToken).ConfigureAwait(false);
        return Results.Ok(persistedSummary is null
            ? new TerminalRunSummary(
                run.Status,
                run.CurrentMcs,
                run.Payload.Metadata.RunIdentity,
                latestMeasurement,
                run.Failure,
                run.RecordingState,
                run.RecordingFailure)
            : persistedSummary with { FinalMeasurement = latestMeasurement });
    }

    private static async Task<IResult> InspectCellAsync(
        Guid id,
        int cellId,
        SimulationSessionRegistry registry,
        CancellationToken cancellationToken)
    {
        if (cellId < 0)
            return Results.NotFound();
        if (!registry.TryGetSession(id, out SimulationSession session))
            return Results.NotFound();
        CellInspection? inspection = await session.InspectCellAsync(cellId, cancellationToken).ConfigureAwait(false);
        return inspection is null ? Results.NotFound() : Results.Ok(inspection);
    }

    private static bool IsTerminal(SimulationSessionStatus status) => status is
        SimulationSessionStatus.Completed or SimulationSessionStatus.Failed or
        SimulationSessionStatus.Interrupted or SimulationSessionStatus.Cancelled;
}
