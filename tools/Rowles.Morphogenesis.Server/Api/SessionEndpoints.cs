using System.Globalization;
using System.Net.WebSockets;
using Rowles.Morphogenesis.Laboratory.Playback;
using Rowles.Morphogenesis.Laboratory.Publication;
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
        sessions.MapGet("/{id:guid}/recording/frame", GetRecordingFrameAsync).WithName("GetRecordingFrame");
        sessions.MapGet("/{id:guid}/stream", StreamFramesAsync).WithName("StreamSessionFrames");
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
            logger.LogWarning(exception, "Rejected session creation for experiment {ExperimentId}", request.ExperimentId);
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (SessionCapacityException exception)
        {
            logger.LogWarning(exception, "Rejected session creation because resident capacity is exhausted for experiment {ExperimentId}", request.ExperimentId);
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
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
        ExecuteCommandAsync(id, request, registry, loggerFactory, "start", SimulationCommandKind.Start, token);

    private static Task<IResult> PauseAsync(Guid id, SessionCommandRequest? request, SimulationSessionRegistry registry, ILoggerFactory loggerFactory, CancellationToken token) =>
        ExecuteCommandAsync(id, request, registry, loggerFactory, "pause", SimulationCommandKind.Pause, token);

    private static Task<IResult> ResumeAsync(Guid id, SessionCommandRequest? request, SimulationSessionRegistry registry, ILoggerFactory loggerFactory, CancellationToken token) =>
        ExecuteCommandAsync(id, request, registry, loggerFactory, "resume", SimulationCommandKind.Resume, token);

    private static Task<IResult> StepAsync(Guid id, SessionCommandRequest? request, SimulationSessionRegistry registry, ILoggerFactory loggerFactory, CancellationToken token) =>
        ExecuteCommandAsync(id, request, registry, loggerFactory, "step", SimulationCommandKind.Step, token);

    private static Task<IResult> StopAsync(Guid id, SessionCommandRequest? request, SimulationSessionRegistry registry, ILoggerFactory loggerFactory, CancellationToken token) =>
        ExecuteCommandAsync(id, request, registry, loggerFactory, "stop", SimulationCommandKind.Stop, token);

    private static async Task<IResult> ExecuteCommandAsync(
        Guid id,
        SessionCommandRequest? request,
        SimulationSessionRegistry registry,
        ILoggerFactory loggerFactory,
        string commandName,
        SimulationCommandKind commandKind,
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

        SimulationCommandResult result;
        try
        {
            result = await registry.ExecuteCommandAsync(
                id,
                commandKind,
                request.CommandId,
                request.ExpectedRevision,
                cancellationToken).ConfigureAwait(false);
        }
        catch (SessionCapacityException exception)
        {
            logger.LogWarning(exception, "Rejected {CommandName} because laboratory session capacity is exhausted for {SessionId}", commandName, id);
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }
        SessionDto? authoritative = await registry.GetAuthoritativeDtoAsync(id, cancellationToken).ConfigureAwait(false);
        if (authoritative is not null)
        {
            using IDisposable experimentContext = LogContext.PushProperty("ExperimentId", authoritative.ExperimentId);
            using IDisposable replicateContext = LogContext.PushProperty("ReplicateId", authoritative.ReplicateId);
            using IDisposable mcsContext = LogContext.PushProperty("Mcs", authoritative.CurrentMcs);
            using IDisposable revisionContext = LogContext.PushProperty("Revision", authoritative.Revision);
            logger.LogInformation("Processed {CommandName} session command as {CommandDisposition}", commandName, result.Disposition);
        }
        return result.Disposition switch
        {
            SimulationCommandDisposition.Applied => Results.Ok(authoritative),
            SimulationCommandDisposition.Failed => Results.Json(
                new
                {
                    error = "The simulation command failed while applying at its boundary.",
                    failure = result.Failure,
                    session = authoritative
                },
                statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Conflict(authoritative)
        };
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

    private static async Task<IResult> GetRecordingFrameAsync(
        Guid id,
        long? mcs,
        SimulationSessionRegistry registry,
        LaboratoryDatabase database,
        IRecordingReader reader,
        RecordingFrameReconstructor reconstructor,
        CancellationToken cancellationToken)
    {
        if (mcs is null or < 0)
            return Results.BadRequest(new { error = "A non-negative mcs query value is required." });

        PersistedRun? run = await database.GetRunAsync(id, cancellationToken).ConfigureAwait(false);
        if (run is null)
            return Results.NotFound();
        if (!run.Payload.Recording.Enabled)
            return Results.Conflict(new { error = "Recording is disabled for this session." });

        try
        {
            using ReconstructedRecordingFrame frame = await reconstructor.ReconstructAsync(
                reader,
                id,
                mcs.Value,
                cancellationToken).ConfigureAwait(false);
            byte[] payload = new byte[FullFrameMessageWriter.GetMessageLength(frame.Width, frame.Height)];
            FullFrameMessageWriter.Write(payload, frame.Sequence, frame.Mcs, frame.Width, frame.Height, frame.CellIds.Span);
            registry.MarkActivity(id);
            return new FullFrameHttpResult(payload, frame.Mcs, frame.Sequence);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (InvalidDataException exception)
        {
            return Results.Problem(
                title: "The stored recording is invalid.",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> StreamFramesAsync(
        HttpContext context,
        Guid id,
        SimulationSessionRegistry registry,
        Rowles.Morphogenesis.Server.Configuration.LaboratoryResourceLimits limits)
    {
        if (!registry.TryGetSession(id, out SimulationSession session))
        {
            SessionDto? persisted = await registry.GetSessionAsync(id, context.RequestAborted).ConfigureAwait(false);
            return persisted is null ? Results.NotFound() : Results.Conflict(persisted);
        }

        if (session.LiveSubscriberCount >= limits.MaxLiveSubscribersPerSession)
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);

        if (!context.WebSockets.IsWebSocketRequest)
            return Results.StatusCode(StatusCodes.Status426UpgradeRequired);

        IAsyncEnumerator<SimulationFrameLease> frames;
        try
        {
            frames = session.WatchFramesAsync(context.RequestAborted).GetAsyncEnumerator(context.RequestAborted);
        }
        catch (InvalidOperationException)
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        await using (frames.ConfigureAwait(false))
        {
            try
            {
                if (!await frames.MoveNextAsync().ConfigureAwait(false))
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            catch (InvalidOperationException)
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            registry.MarkActivity(id);
            byte[] message = new byte[FullFrameMessageWriter.GetMessageLength(session.Metadata.GridWidth, session.Metadata.GridHeight)];
            try
            {
                do
                {
                    SimulationFrameLease frame = frames.Current;
                    try
                    {
                        FullFrameMessageWriter.Write(message, frame);
                        await socket.SendAsync(
                            message.AsMemory(),
                            WebSocketMessageType.Binary,
                            endOfMessage: true,
                            context.RequestAborted).ConfigureAwait(false);
                    }
                    finally
                    {
                        frame.Dispose();
                    }
                }
                while (await frames.MoveNextAsync().ConfigureAwait(false));

                if (socket.State == WebSocketState.Open)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session stream ended.", CancellationToken.None).ConfigureAwait(false);
            }
            catch (WebSocketException) when (context.RequestAborted.IsCancellationRequested || socket.State is WebSocketState.Aborted or WebSocketState.Closed)
            {
                // A remote disconnect only closes this subscription; it does not change session state.
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The request ended; the simulation remains owned by the session registry.
            }
            catch (ObjectDisposedException)
            {
                // The session was retired while this stream was waiting for another frame.
            }

            return Results.Empty;
        }
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
        if (inspection is not null)
            registry.MarkActivity(id);
        return inspection is null ? Results.NotFound() : Results.Ok(inspection);
    }

    private static bool IsTerminal(SimulationSessionStatus status) => status is
        SimulationSessionStatus.Completed or SimulationSessionStatus.Failed or
        SimulationSessionStatus.Interrupted or SimulationSessionStatus.Cancelled;

    private sealed class FullFrameHttpResult(byte[] payload, long mcs, long sequence) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "application/octet-stream";
            httpContext.Response.ContentLength = payload.Length;
            httpContext.Response.Headers["X-Morphogenesis-Frame-Protocol"] = FullFrameMessageWriter.ProtocolVersion.ToString(CultureInfo.InvariantCulture);
            httpContext.Response.Headers["X-Morphogenesis-Frame-Mcs"] = mcs.ToString(CultureInfo.InvariantCulture);
            httpContext.Response.Headers["X-Morphogenesis-Frame-Sequence"] = sequence.ToString(CultureInfo.InvariantCulture);
            await httpContext.Response.Body.WriteAsync(payload, httpContext.RequestAborted).ConfigureAwait(false);
        }
    }
}
