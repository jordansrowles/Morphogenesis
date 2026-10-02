using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Server.Experiments;
using Rowles.Morphogenesis.Server.Persistence;
using Rowles.Morphogenesis.Server.Sessions;
using Rowles.Morphogenesis.Server.Tests.Testing;
using Xunit;

namespace Rowles.Morphogenesis.Server.Tests.Api;

public sealed class LaboratoryApiTests
{
    private const string SortingExperimentId = "E02-sorting-v1";

    [Fact]
    public async Task HealthCatalogueAndVersionOneSchemaAreAvailable()
    {
        using TemporaryLaboratoryRoot root = new();
        using LaboratoryFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using HttpResponseMessage info = await client.GetAsync("/api/info");
        Assert.Equal(HttpStatusCode.OK, info.StatusCode);
        using HttpResponseMessage experiments = await client.GetAsync("/api/experiments");
        Assert.Equal(HttpStatusCode.OK, experiments.StatusCode);
        string json = await experiments.Content.ReadAsStringAsync();
        Assert.Contains(SortingExperimentId, json, StringComparison.Ordinal);

        LaboratoryDatabase database = factory.Services.GetRequiredService<LaboratoryDatabase>();
        Assert.Equal(1, await database.GetUserVersionAsync());
        await using SqliteConnection connection = new($"Data Source={factory.DatabasePath};Mode=ReadOnly");
        await connection.OpenAsync();
        await using (SqliteCommand journal = connection.CreateCommand())
        {
            journal.CommandText = "PRAGMA journal_mode;";
            Assert.Equal("wal", (string?)await journal.ExecuteScalarAsync());
        }
        await using SqliteCommand schema = connection.CreateCommand();
        schema.CommandText = "SELECT type, name FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type, name;";
        List<(string Type, string Name)> objects = [];
        await using SqliteDataReader reader = await schema.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            objects.Add((reader.GetString(0), reader.GetString(1)));
        Assert.Equal(
            new[] { ("index", "IX_Frames_RunId_Mcs"), ("table", "Frames"), ("table", "Metrics"), ("table", "Runs") },
            objects);

        SqliteWriteQueue writeQueue = factory.Services.GetRequiredService<SqliteWriteQueue>();
        Assert.Equal(1, await ReadWritePragmaAsync(writeQueue, "foreign_keys"));
        Assert.Equal(5_000, await ReadWritePragmaAsync(writeQueue, "busy_timeout"));
        Assert.Equal(1, await ReadWritePragmaAsync(writeQueue, "synchronous"));
    }

    [Fact]
    public async Task CreationValidatesFpsAndGridDimensions()
    {
        using (TemporaryLaboratoryRoot root = new())
        using (LaboratoryFactory factory = new(root.Path))
        using (HttpClient client = factory.CreateClient())
        {
            using HttpResponseMessage invalidFps = await client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest(SortingExperimentId, 0, false, 21));
            Assert.Equal(HttpStatusCode.BadRequest, invalidFps.StatusCode);
        }

        using TemporaryLaboratoryRoot largeRoot = new();
        using LaboratoryFactory largeFactory = new(largeRoot.Path, mutateCatalogue: directory =>
        {
            string path = Path.Combine(directory, "E02-sorting.json");
            var manifest = Rowles.Morphogenesis.Experiments.ExperimentManifest.ReadJson(path);
            File.WriteAllText(path, (manifest with { GridWidth = 513 }).ToJson());
        });
        using HttpClient largeClient = largeFactory.CreateClient();
        ExperimentCatalog largeCatalog = largeFactory.Services.GetRequiredService<ExperimentCatalog>();
        Assert.Equal(513, largeCatalog.Entries.Single(entry => entry.ExperimentId == SortingExperimentId).GridWidth);
        using HttpResponseMessage oversized = await largeClient.PostAsJsonAsync("/api/sessions", new CreateSessionRequest(SortingExperimentId));
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
    }

    [Fact]
    public async Task SimultaneousCommandsAtOneRevisionReturnOneConflictAndPersistMetrics()
    {
        using TemporaryLaboratoryRoot root = new();
        using LaboratoryFactory factory = new(root.Path);
        using HttpClient firstClient = factory.CreateClient();
        using HttpClient secondClient = factory.CreateClient();
        SessionDto created = await CreateSessionAsync(firstClient, recording: false);

        using HttpResponseMessage startResponse = await firstClient.PostAsJsonAsync(
            $"/api/sessions/{created.SessionId:D}/start",
            new SessionCommandRequest(Guid.NewGuid(), created.Revision));
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        SessionDto running = (await startResponse.Content.ReadFromJsonAsync<SessionDto>())!;
        Assert.Equal(SimulationSessionStatus.Running, running.Status);

        Task<HttpResponseMessage> firstPause = firstClient.PostAsJsonAsync(
            $"/api/sessions/{created.SessionId:D}/pause",
            new SessionCommandRequest(Guid.NewGuid(), running.Revision));
        Task<HttpResponseMessage> secondPause = secondClient.PostAsJsonAsync(
            $"/api/sessions/{created.SessionId:D}/pause",
            new SessionCommandRequest(Guid.NewGuid(), running.Revision));
        HttpResponseMessage[] pauseResponses = await Task.WhenAll(firstPause, secondPause);
        try
        {
            Assert.Single(pauseResponses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(pauseResponses, response => response.StatusCode == HttpStatusCode.Conflict);
            HttpResponseMessage conflict = pauseResponses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            SessionDto authoritative = (await conflict.Content.ReadFromJsonAsync<SessionDto>())!;
            Assert.Equal(SimulationSessionStatus.Paused, authoritative.Status);
            Assert.Equal(running.Revision + 1, authoritative.Revision);

            using HttpResponseMessage metricsResponse = await firstClient.GetAsync($"/api/sessions/{created.SessionId:D}/metrics");
            Assert.Equal(HttpStatusCode.OK, metricsResponse.StatusCode);
            string metricsJson = await metricsResponse.Content.ReadAsStringAsync();
            Assert.Contains("heterotypic-interface-count", metricsJson, StringComparison.Ordinal);
            Assert.Contains("type-domain-count.type-1", metricsJson, StringComparison.Ordinal);

            LaboratoryDatabase database = factory.Services.GetRequiredService<LaboratoryDatabase>();
            PersistedRun persisted = (await database.GetRunAsync(created.SessionId))!;
            SimulationSessionRegistry registry = factory.Services.GetRequiredService<SimulationSessionRegistry>();
            Assert.True(registry.TryGetSession(created.SessionId, out SimulationSession liveSession));
            Task update = database.UpdateSnapshotAsync(created.SessionId, liveSession.GetSnapshot());
            Task<PersistedRun?> read = database.GetRunAsync(created.SessionId);
            await Task.WhenAll(update, read);
            Assert.NotNull(await read);
            Assert.NotEqual(Guid.Empty, persisted.Id);
        }
        finally
        {
            foreach (HttpResponseMessage response in pauseResponses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task CommandEndpointsApplyLifecycleAndReturnTheFinalMeasurement()
    {
        using TemporaryLaboratoryRoot root = new();
        using LaboratoryFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        SessionDto state = await CreateSessionAsync(client, recording: false);

        using HttpResponseMessage createdResult = await client.GetAsync($"/api/sessions/{state.SessionId:D}/result");
        Assert.Equal(HttpStatusCode.NoContent, createdResult.StatusCode);

        state = await SendCommandAsync(client, state, "start");
        Assert.Equal(SimulationSessionStatus.Running, state.Status);
        state = await SendCommandAsync(client, state, "pause");
        Assert.Equal(SimulationSessionStatus.Paused, state.Status);
        long pausedMcs = state.CurrentMcs;
        state = await SendCommandAsync(client, state, "step");
        Assert.Equal(SimulationSessionStatus.Paused, state.Status);
        Assert.Equal(pausedMcs + 1, state.CurrentMcs);
        state = await SendCommandAsync(client, state, "resume");
        Assert.Equal(SimulationSessionStatus.Running, state.Status);
        state = await SendCommandAsync(client, state, "stop");
        Assert.Equal(SimulationSessionStatus.Cancelled, state.Status);

        using HttpResponseMessage result = await client.GetAsync($"/api/sessions/{state.SessionId:D}/result");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        TerminalRunSummary summary = (await result.Content.ReadFromJsonAsync<TerminalRunSummary>())!;
        Assert.Equal(SimulationSessionStatus.Cancelled, summary.Status);
        Assert.Equal(state.CurrentMcs, summary.FinalMcs);
        Assert.NotNull(summary.FinalMeasurement);
        Assert.Contains("heterotypic-interface-count", summary.FinalMeasurement.Values.Keys);
    }

    [Fact]
    public async Task RecordingFramesCanBeReadAndDuplicateSequenceIsRejected()
    {
        using TemporaryLaboratoryRoot root = new();
        using LaboratoryFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        SessionDto session = await CreateSessionAsync(client, recording: true);
        SqliteRecordingReader reader = factory.Services.GetRequiredService<SqliteRecordingReader>();

        IReadOnlyList<RecordingFrameIndexEntry> index = [];
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (index.Count == 0)
        {
            timeout.Token.ThrowIfCancellationRequested();
            index = await reader.GetFrameIndexAsync(session.SessionId, timeout.Token);
            if (index.Count == 0)
                await Task.Delay(25, timeout.Token);
        }

        Assert.Equal(new RecordingFrameIndexEntry(0, 0, RecordingFrameKind.Keyframe), index[0]);
        EncodedRecordingFrame frame = await reader.ReadFrameAsync(session.SessionId, 0, timeout.Token);
        IRecordingStore store = factory.Services.GetRequiredService<SqliteRecordingStore>();
        await Assert.ThrowsAsync<SqliteException>(async () =>
            await store.AppendFrameAsync(session.SessionId, frame, timeout.Token));

        using HttpResponseMessage recordingResponse = await client.GetAsync($"/api/sessions/{session.SessionId:D}/recording");
        Assert.Equal(HttpStatusCode.OK, recordingResponse.StatusCode);
        string recordingJson = await recordingResponse.Content.ReadAsStringAsync();
        Assert.Contains("messagepack-lz4-block-array", recordingJson, StringComparison.Ordinal);
        Assert.Contains("frameCount", recordingJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunningSessionBecomesInterruptedOnNextProcessStart()
    {
        using TemporaryLaboratoryRoot root = new();
        Guid sessionId;
        LaboratoryFactory firstFactory = new(root.Path);
        using (HttpClient client = firstFactory.CreateClient())
        {
            SessionDto created = await CreateSessionAsync(client, recording: false);
            sessionId = created.SessionId;
            using HttpResponseMessage start = await client.PostAsJsonAsync(
                $"/api/sessions/{sessionId:D}/start",
                new SessionCommandRequest(Guid.NewGuid(), created.Revision));
            Assert.Equal(HttpStatusCode.OK, start.StatusCode);
            await WaitForRunStatusAsync(firstFactory, sessionId, SimulationSessionStatus.Running);
        }

        firstFactory.Dispose();
        using LaboratoryFactory restartedFactory = new(root.Path);
        using HttpClient restartedClient = restartedFactory.CreateClient();
        SessionDto recovered = (await restartedClient.GetFromJsonAsync<SessionDto>($"/api/sessions/{sessionId:D}"))!;
        Assert.Equal(SimulationSessionStatus.Interrupted, recovered.Status);
        Assert.False(restartedFactory.Services.GetRequiredService<SimulationSessionRegistry>().TryGetSession(sessionId, out _));
        using HttpResponseMessage result = await restartedClient.GetAsync($"/api/sessions/{sessionId:D}/result");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        string resultJson = await result.Content.ReadAsStringAsync();
        Assert.Contains("finalMeasurement", resultJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("heterotypic-interface-count", resultJson, StringComparison.Ordinal);
        using HttpResponseMessage cells = await restartedClient.GetAsync($"/api/sessions/{sessionId:D}/cells/1");
        Assert.Equal(HttpStatusCode.NotFound, cells.StatusCode);
    }

    [Fact]
    public async Task UnsupportedSchemaAndDuplicateCatalogueIdFailStartup()
    {
        using TemporaryLaboratoryRoot schemaRoot = new();
        using (LaboratoryFactory schemaFactory = new(schemaRoot.Path))
        {
            Directory.CreateDirectory(schemaFactory.DataDirectory);
            await using SqliteConnection connection = new($"Data Source={schemaFactory.DatabasePath}");
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 9;";
            await command.ExecuteNonQueryAsync();
        }

        using (LaboratoryFactory schemaFactory = new(schemaRoot.Path))
        {
            Assert.ThrowsAny<Exception>(() => schemaFactory.CreateClient());
        }

        using TemporaryLaboratoryRoot duplicateRoot = new();
        using LaboratoryFactory duplicateFactory = new(duplicateRoot.Path, mutateCatalogue: directory =>
        {
            string source = Path.Combine(directory, "E00-single-cell-relaxation.json");
            var manifest = Rowles.Morphogenesis.Experiments.ExperimentManifest.ReadJson(source);
            File.WriteAllText(source, (manifest with { ExperimentId = SortingExperimentId }).ToJson());
        });
        Assert.ThrowsAny<Exception>(() => duplicateFactory.CreateClient());
    }

    [Fact]
    public async Task UnknownSessionReturnsNotFound()
    {
        using TemporaryLaboratoryRoot root = new();
        using LaboratoryFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        Guid unknown = Guid.NewGuid();
        using HttpResponseMessage response = await client.GetAsync($"/api/sessions/{unknown:D}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<SessionDto> CreateSessionAsync(HttpClient client, bool recording)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/sessions",
            new CreateSessionRequest(SortingExperimentId, 0, recording, 10));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SessionDto>())!;
    }

    private static async Task<SessionDto> SendCommandAsync(HttpClient client, SessionDto current, string command)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/sessions/{current.SessionId:D}/{command}",
            new SessionCommandRequest(Guid.NewGuid(), current.Revision));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SessionDto>())!;
    }

    private static Task<int> ReadWritePragmaAsync(SqliteWriteQueue writeQueue, string pragma) =>
        writeQueue.ExecuteAsync(async (connection, cancellationToken) =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA {pragma};";
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }).AsTask();

    private static async Task WaitForRunStatusAsync(
        LaboratoryFactory factory,
        Guid sessionId,
        SimulationSessionStatus status)
    {
        LaboratoryDatabase database = factory.Services.GetRequiredService<LaboratoryDatabase>();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (true)
        {
            PersistedRun? run = await database.GetRunAsync(sessionId, timeout.Token);
            if (run?.Status == status)
                return;
            await Task.Delay(25, timeout.Token);
        }
    }
}
