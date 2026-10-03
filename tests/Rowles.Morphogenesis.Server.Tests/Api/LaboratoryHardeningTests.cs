using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Server.Configuration;
using Rowles.Morphogenesis.Server.Diagnostics;
using Rowles.Morphogenesis.Server.Experiments;
using Rowles.Morphogenesis.Server.Persistence;
using Rowles.Morphogenesis.Server.Services;
using Rowles.Morphogenesis.Server.Sessions;
using Rowles.Morphogenesis.Server.Tests.Testing;
using Xunit;

namespace Rowles.Morphogenesis.Server.Tests.Api;

public sealed class LaboratoryHardeningTests
{
    private const string SortingExperimentId = "E02-sorting-v1";

    [Fact]
    public async Task DiagnosticsAreExposedWithoutFrameOrRecordingPayloads()
    {
        using TemporaryLaboratoryRoot root = new();
        using LaboratoryFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/diagnostics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync();
        Assert.Contains("activeSessions", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mcsPerSecond", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sqliteWriterQueueDepth", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cellIds", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", json, StringComparison.OrdinalIgnoreCase);

        LaboratoryDiagnostics diagnostics = factory.Services.GetRequiredService<LaboratoryDiagnostics>();
        Assert.Equal(0, diagnostics.Capture([], 0).ActiveSessions);
    }

    [Fact]
    public async Task RunningCapacityReturns429AndStepUsesOnlyATransientSlot()
    {
        using TemporaryLaboratoryRoot root = new();
        LaboratoryResourceLimits limits = new() { MaxRunningSessions = 1 };
        using LaboratoryFactory factory = new(root.Path, resourceLimits: limits);
        using HttpClient client = factory.CreateClient();
        SessionDto first = await CreateSessionAsync(client, recording: false);
        SessionDto second = await CreateSessionAsync(client, recording: false);

        using HttpResponseMessage started = await SendCommandResponseAsync(client, first, "start");
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        SessionDto running = (await started.Content.ReadFromJsonAsync<SessionDto>())!;
        Assert.Equal(SimulationSessionStatus.Running, running.Status);

        using HttpResponseMessage refusedStep = await SendCommandResponseAsync(client, second, "step");
        Assert.Equal(HttpStatusCode.TooManyRequests, refusedStep.StatusCode);

        using HttpResponseMessage paused = await SendCommandResponseAsync(client, running, "pause");
        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        SessionDto pausedSession = (await paused.Content.ReadFromJsonAsync<SessionDto>())!;
        using HttpResponseMessage stepped = await SendCommandResponseAsync(client, second, "step");
        Assert.Equal(HttpStatusCode.OK, stepped.StatusCode);
        SessionDto steppedSession = (await stepped.Content.ReadFromJsonAsync<SessionDto>())!;
        Assert.Equal(SimulationSessionStatus.Paused, steppedSession.Status);
        Assert.Equal(second.CurrentMcs + 1, steppedSession.CurrentMcs);
        Assert.Equal(SimulationSessionStatus.Paused, pausedSession.Status);
    }

    [Fact]
    public async Task PausingBeyondCapacityCancelsTheOldestPausedSessionWithPolicyReason()
    {
        using TemporaryLaboratoryRoot root = new();
        LaboratoryResourceLimits limits = new() { MaxPausedSessions = 1 };
        using LaboratoryFactory factory = new(root.Path, resourceLimits: limits);
        using HttpClient client = factory.CreateClient();
        SessionDto first = await CreateSessionAsync(client, recording: false);
        SessionDto second = await CreateSessionAsync(client, recording: false);

        first = await SendCommandAsync(client, first, "start");
        first = await SendCommandAsync(client, first, "pause");
        second = await SendCommandAsync(client, second, "start");
        second = await SendCommandAsync(client, second, "pause");

        LaboratoryDatabase database = factory.Services.GetRequiredService<LaboratoryDatabase>();
        PersistedRun cancelled = (await database.GetRunAsync(first.SessionId))!;
        Assert.Equal(SimulationSessionStatus.Cancelled, cancelled.Status);
        Assert.Contains("ResourcePolicy: MaximumPausedSessionsExceeded", cancelled.Failure, StringComparison.Ordinal);
        Assert.False(factory.Services.GetRequiredService<SimulationSessionRegistry>().TryGetSession(first.SessionId, out _));
        Assert.Equal(SimulationSessionStatus.Paused, second.Status);
    }

    [Fact]
    public async Task CreatedIdleSessionIsPersistedAsCancelledAndRemoved()
    {
        using TemporaryLaboratoryRoot root = new();
        using LaboratoryFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        SessionDto created = await CreateSessionAsync(client, recording: false);
        SessionRetentionService retention = factory.Services.GetRequiredService<SessionRetentionService>();

        RetentionSweepResult result = await retention.SweepAsync(created.CreatedAtUtc.AddHours(2));

        Assert.Equal(1, result.IdleSessionsCancelled);
        PersistedRun persisted = (await factory.Services.GetRequiredService<LaboratoryDatabase>()
            .GetRunAsync(created.SessionId))!;
        Assert.Equal(SimulationSessionStatus.Cancelled, persisted.Status);
        Assert.Contains("ResourcePolicy: CreatedSessionIdleTimeout", persisted.Failure, StringComparison.Ordinal);
        Assert.False(factory.Services.GetRequiredService<SimulationSessionRegistry>().TryGetSession(created.SessionId, out _));
    }

    [Fact]
    public async Task RetentionDeletesOldTerminalRowsAndCascadesRecordingAndMetrics()
    {
        using TemporaryLaboratoryRoot root = new();
        using LaboratoryFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        SessionDto created = await CreateSessionAsync(client, recording: true);
        SqliteRecordingReader reader = factory.Services.GetRequiredService<SqliteRecordingReader>();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        IReadOnlyList<RecordingFrameIndexEntry> index = [];
        while (index.Count == 0)
        {
            index = await reader.GetFrameIndexAsync(created.SessionId, timeout.Token);
            if (index.Count == 0)
                await Task.Delay(25, timeout.Token);
        }

        SessionDto cancelled = await SendCommandAsync(client, created, "stop");
        Assert.Equal(SimulationSessionStatus.Cancelled, cancelled.Status);
        LaboratoryDatabase database = factory.Services.GetRequiredService<LaboratoryDatabase>();
        await WaitForPersistedStatusAsync(database, created.SessionId, SimulationSessionStatus.Cancelled);
        Assert.NotEmpty(await database.GetMetricsAsync(created.SessionId));
        Assert.NotEmpty(await reader.GetFrameIndexAsync(created.SessionId));
        SqliteWriteQueue writeQueue = factory.Services.GetRequiredService<SqliteWriteQueue>();
        await SetCompletedAtAsync(writeQueue, created.SessionId, DateTimeOffset.UtcNow.AddDays(-31));

        RetentionSweepResult result = await factory.Services.GetRequiredService<SessionRetentionService>()
            .SweepAsync(DateTimeOffset.UtcNow);

        Assert.Equal(1, result.PersistedRunsDeleted);
        Assert.Null(await database.GetRunAsync(created.SessionId));
        Assert.Empty(await database.GetMetricsAsync(created.SessionId));
        Assert.Empty(await reader.GetFrameIndexAsync(created.SessionId));
        Assert.False(factory.Services.GetRequiredService<SimulationSessionRegistry>().TryGetSession(created.SessionId, out _));
    }

    [Fact]
    public async Task CountCapDeletesOldestTerminalRunsButNeverAnActiveRun()
    {
        using TemporaryLaboratoryRoot root = new();
        LaboratoryResourceLimits limits = new() { MaxPersistedRuns = 1 };
        using LaboratoryFactory factory = new(root.Path, resourceLimits: limits);
        using HttpClient client = factory.CreateClient();
        SessionDto first = await CreateSessionAsync(client, recording: false);
        SessionDto second = await CreateSessionAsync(client, recording: false);
        SessionDto running = await CreateSessionAsync(client, recording: false);
        first = await SendCommandAsync(client, first, "stop");
        second = await SendCommandAsync(client, second, "stop");
        running = await SendCommandAsync(client, running, "start");
        LaboratoryDatabase database = factory.Services.GetRequiredService<LaboratoryDatabase>();
        await WaitForPersistedStatusAsync(database, first.SessionId, SimulationSessionStatus.Cancelled);
        await WaitForPersistedStatusAsync(database, second.SessionId, SimulationSessionStatus.Cancelled);
        await WaitForPersistedStatusAsync(database, running.SessionId, SimulationSessionStatus.Running);
        Assert.Equal(SimulationSessionStatus.Running, running.Status);

        RetentionSweepResult result = await factory.Services.GetRequiredService<SessionRetentionService>()
            .SweepAsync(DateTimeOffset.UtcNow);

        Assert.Equal(2, result.PersistedRunsDeleted);
        Assert.Null(await database.GetRunAsync(first.SessionId));
        Assert.Null(await database.GetRunAsync(second.SessionId));
        Assert.Equal(SimulationSessionStatus.Running, (await database.GetRunAsync(running.SessionId))!.Status);
        Assert.Equal(1, result.RemainingRunCount);
    }

    [Fact]
    public async Task StoredByteCapDeletesOldestTerminalRunsUntilUnderLimit()
    {
        using TemporaryLaboratoryRoot root = new();
        LaboratoryResourceLimits limits = new() { MaxStoredRecordingBytes = 5 };
        using LaboratoryFactory factory = new(root.Path, resourceLimits: limits);
        using HttpClient client = factory.CreateClient();
        SessionDto first = await CreateSessionAsync(client, recording: false);
        SessionDto second = await CreateSessionAsync(client, recording: false);
        _ = await SendCommandAsync(client, first, "stop");
        _ = await SendCommandAsync(client, second, "stop");
        LaboratoryDatabase database = factory.Services.GetRequiredService<LaboratoryDatabase>();
        await WaitForPersistedStatusAsync(database, first.SessionId, SimulationSessionStatus.Cancelled);
        await WaitForPersistedStatusAsync(database, second.SessionId, SimulationSessionStatus.Cancelled);
        SqliteWriteQueue writeQueue = factory.Services.GetRequiredService<SqliteWriteQueue>();
        await SetRecordingBytesAsync(writeQueue, first.SessionId, 8);
        await SetRecordingBytesAsync(writeQueue, second.SessionId, 4);
        await SetCompletedAtAsync(writeQueue, first.SessionId, DateTimeOffset.UtcNow.AddHours(-2));
        await SetCompletedAtAsync(writeQueue, second.SessionId, DateTimeOffset.UtcNow.AddHours(-1));

        RetentionSweepResult result = await factory.Services.GetRequiredService<SessionRetentionService>()
            .SweepAsync(DateTimeOffset.UtcNow);

        Assert.Equal(1, result.PersistedRunsDeleted);
        Assert.Null(await database.GetRunAsync(first.SessionId));
        Assert.NotNull(await database.GetRunAsync(second.SessionId));
        Assert.Equal(4, result.RemainingRecordingBytes);
    }

    [Fact]
    public async Task RecordingStorageLimitFailsRecordingWithoutStoppingSimulationOrInsertingPartialFrame()
    {
        using TemporaryLaboratoryRoot root = new();
        LaboratoryResourceLimits limits = new() { MaxRecordingBytesPerRun = 1 };
        using LaboratoryFactory factory = new(root.Path, longRunning: false, resourceLimits: limits,
            mutateCatalogue: directory =>
            {
                string path = Path.Combine(directory, "E02-sorting.json");
                var manifest = Rowles.Morphogenesis.Experiments.ExperimentManifest.ReadJson(path);
                File.WriteAllText(path, (manifest with { McsCount = 2 }).ToJson());
            });
        using HttpClient client = factory.CreateClient();
        SessionDto created = await CreateSessionAsync(client, recording: true);
        SimulationSessionRegistry registry = factory.Services.GetRequiredService<SimulationSessionRegistry>();
        Assert.True(registry.TryGetSession(created.SessionId, out SimulationSession session));
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (session.GetSnapshot().RecordingState != RecordingState.Failed)
            await Task.Delay(10, timeout.Token);

        SimulationSessionSnapshot failedRecording = session.GetSnapshot();
        Assert.Equal(SimulationSessionStatus.Created, failedRecording.Status);
        Assert.Contains("RecordingStorageLimitExceeded", failedRecording.RecordingFailure, StringComparison.Ordinal);
        Assert.Empty(await factory.Services.GetRequiredService<SqliteRecordingReader>()
            .GetFrameIndexAsync(created.SessionId, timeout.Token));

        _ = await SendCommandAsync(client, created, "start");
        SimulationSessionSnapshot completed = await WaitForStatusAsync(session, SimulationSessionStatus.Completed, timeout.Token);
        Assert.Equal(2, completed.CurrentMcs);
        Assert.Equal(RecordingState.Failed, completed.RecordingState);
    }

    [Fact]
    public async Task TotalRecordingStorageLimitFailsRecordingWithoutStoppingSimulation()
    {
        using TemporaryLaboratoryRoot root = new();
        LaboratoryResourceLimits limits = new() { MaxStoredRecordingBytes = 1 };
        using LaboratoryFactory factory = new(root.Path, longRunning: false, resourceLimits: limits,
            mutateCatalogue: directory =>
            {
                string path = Path.Combine(directory, "E02-sorting.json");
                var manifest = Rowles.Morphogenesis.Experiments.ExperimentManifest.ReadJson(path);
                File.WriteAllText(path, (manifest with { McsCount = 2 }).ToJson());
            });
        using HttpClient client = factory.CreateClient();
        SessionDto created = await CreateSessionAsync(client, recording: true);
        SimulationSessionRegistry registry = factory.Services.GetRequiredService<SimulationSessionRegistry>();
        Assert.True(registry.TryGetSession(created.SessionId, out SimulationSession session));
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (session.GetSnapshot().RecordingState != RecordingState.Failed)
            await Task.Delay(10, timeout.Token);

        SimulationSessionSnapshot failedRecording = session.GetSnapshot();
        Assert.Equal(SimulationSessionStatus.Created, failedRecording.Status);
        Assert.Contains("RecordingStorageLimitExceeded", failedRecording.RecordingFailure, StringComparison.Ordinal);
        Assert.Empty(await factory.Services.GetRequiredService<SqliteRecordingReader>()
            .GetFrameIndexAsync(created.SessionId, timeout.Token));

        _ = await SendCommandAsync(client, created, "start");
        SimulationSessionSnapshot completed = await WaitForStatusAsync(session, SimulationSessionStatus.Completed, timeout.Token);
        Assert.Equal(2, completed.CurrentMcs);
        Assert.Equal(RecordingState.Failed, completed.RecordingState);
    }

    [Fact]
    public async Task RequestAndCatalogueIdentifiersRejectOversizeBodiesAndPathLikeIds()
    {
        using TemporaryLaboratoryRoot root = new();
        using LaboratoryFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage pathLikeId = await client.GetAsync("/api/experiments/..%2F..%2Fexperiments%2Fcanonical%2FE02-control.json");
        Assert.NotEqual(HttpStatusCode.OK, pathLikeId.StatusCode);
        using HttpResponseMessage malformedGuid = await client.GetAsync("/api/sessions/not-a-guid");
        Assert.Equal(HttpStatusCode.NotFound, malformedGuid.StatusCode);
        using HttpResponseMessage invalidRevision = await client.PostAsJsonAsync(
            $"/api/sessions/{Guid.NewGuid():D}/start",
            new SessionCommandRequest(Guid.NewGuid(), -1));
        Assert.Equal(HttpStatusCode.BadRequest, invalidRevision.StatusCode);

        using ByteArrayContent oversizedBody = new(new byte[LaboratoryResourceLimits.HardMaxRequestBodyBytes + 1]);
        oversizedBody.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using HttpResponseMessage oversized = await client.PostAsync("/api/sessions", oversizedBody);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
    }

    [Fact]
    public void ManifestFilesAreLimitedBeforeParsing()
    {
        using TemporaryLaboratoryRoot root = new();
        LaboratoryResourceLimits limits = new() { MaxManifestBytes = 1 };
        using LaboratoryFactory factory = new(root.Path, resourceLimits: limits);

        LaboratoryServerOptions options = new()
        {
            CanonicalExperimentsDirectory = factory.CanonicalDirectory,
            ResourceLimits = limits
        };
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => new ExperimentCatalog(options));
        Assert.Contains("exceeds the configured manifest size limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductionProblemDetailsDoNotExposeExceptionStackTraces()
    {
        using TemporaryLaboratoryRoot root = new();
        using LaboratoryFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        SessionDto created = await CreateSessionAsync(client, recording: false);
        await SetResultJsonAsync(factory.DatabasePath, created.SessionId, "{");

        using HttpResponseMessage response = await client.GetAsync($"/api/sessions/{created.SessionId:D}");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("JsonException", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Text.Json", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at Rowles.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedFrameVersionAndCorruptSqlitePayloadAreRejected()
    {
        using TemporaryLaboratoryRoot root = new();
        using LaboratoryFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        SessionDto session = await CreateSessionAsync(client, recording: true);
        SqliteRecordingReader reader = factory.Services.GetRequiredService<SqliteRecordingReader>();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while ((await reader.GetFrameIndexAsync(session.SessionId, timeout.Token)).Count == 0)
            await Task.Delay(25, timeout.Token);

        await UpdateFrameAsync(factory.DatabasePath, session.SessionId, "EnvelopeVersion = 99");
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await reader.ReadFrameAsync(session.SessionId, 0, timeout.Token));

        await UpdateFrameAsync(factory.DatabasePath, session.SessionId, "EnvelopeVersion = 1, Payload = X'00'");
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await reader.ReadFrameAsync(session.SessionId, 0, timeout.Token));
    }

    private static async Task<SessionDto> CreateSessionAsync(HttpClient client, bool recording) =>
        await PostSessionAsync(client, new CreateSessionRequest(SortingExperimentId, 0, recording, 10));

    private static async Task<SessionDto> PostSessionAsync(HttpClient client, CreateSessionRequest request)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/sessions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SessionDto>())!;
    }

    private static async Task<SessionDto> SendCommandAsync(HttpClient client, SessionDto current, string command)
    {
        using HttpResponseMessage response = await SendCommandResponseAsync(client, current, command);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SessionDto>())!;
    }

    private static Task<HttpResponseMessage> SendCommandResponseAsync(HttpClient client, SessionDto current, string command) =>
        client.PostAsJsonAsync(
            $"/api/sessions/{current.SessionId:D}/{command}",
            new SessionCommandRequest(Guid.NewGuid(), current.Revision));

    private static async Task SetCompletedAtAsync(SqliteWriteQueue writeQueue, Guid sessionId, DateTimeOffset completedAtUtc)
    {
        int rows = await writeQueue.ExecuteAsync(async (connection, cancellationToken) =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE Runs SET CompletedAtUtc = $completed WHERE Id = $id;";
            command.Parameters.AddWithValue("$completed", completedAtUtc.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
            return await command.ExecuteNonQueryAsync(cancellationToken);
        });
        Assert.Equal(1, rows);
    }

    private static async Task SetRecordingBytesAsync(SqliteWriteQueue writeQueue, Guid sessionId, long recordingBytes)
    {
        int rows = await writeQueue.ExecuteAsync(async (connection, cancellationToken) =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE Runs SET RecordingBytes = $bytes WHERE Id = $id;";
            command.Parameters.AddWithValue("$bytes", recordingBytes);
            command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
            return await command.ExecuteNonQueryAsync(cancellationToken);
        });
        Assert.Equal(1, rows);
    }

    private static async Task SetResultJsonAsync(string databasePath, Guid sessionId, string resultJson)
    {
        await using SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadWrite");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE Runs SET ResultJson = $result WHERE Id = $id;";
        command.Parameters.AddWithValue("$result", resultJson);
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task UpdateFrameAsync(string databasePath, Guid sessionId, string assignments)
    {
        await using SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadWrite");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"UPDATE Frames SET {assignments} WHERE RunId = $id AND Sequence = 0;";
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<SimulationSessionSnapshot> WaitForStatusAsync(
        SimulationSession session,
        SimulationSessionStatus status,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SimulationSessionSnapshot snapshot = session.GetSnapshot();
            if (snapshot.Status == status)
                return snapshot;
            await Task.Delay(10, cancellationToken);
        }
    }

    private static async Task WaitForPersistedStatusAsync(
        LaboratoryDatabase database,
        Guid sessionId,
        SimulationSessionStatus status)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while ((await database.GetRunAsync(sessionId, timeout.Token))?.Status != status)
            await Task.Delay(10, timeout.Token);
    }
}
