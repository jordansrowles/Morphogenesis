using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Results;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Server.Configuration;

namespace Rowles.Morphogenesis.Server.Persistence;

public sealed class LaboratoryDatabase
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LaboratoryServerOptions _options;
    private readonly SqliteWriteQueue _writeQueue;
    private readonly string _readConnectionString;

    public LaboratoryDatabase(LaboratoryServerOptions options, SqliteWriteQueue writeQueue)
    {
        _options = options;
        _writeQueue = writeQueue;
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = options.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        };
        _readConnectionString = builder.ToString();
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        _writeQueue.ExecuteAsync(async (connection, token) =>
        {
            await using (SqliteCommand journal = connection.CreateCommand())
            {
                journal.CommandText = "PRAGMA journal_mode = WAL;";
                await journal.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            int currentVersion = await ReadUserVersionAsync(connection, token).ConfigureAwait(false);
            if (currentVersion == SchemaVersion)
                return true;
            if (currentVersion != 0)
                throw new UnsupportedDatabaseSchemaException(currentVersion, SchemaVersion);

            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = SchemaSql;
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            await using (SqliteCommand version = connection.CreateCommand())
            {
                version.Transaction = transaction;
                version.CommandText = $"PRAGMA user_version = {SchemaVersion.ToString(CultureInfo.InvariantCulture)};";
                await version.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            await transaction.CommitAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken).AsTask();

    public Task<int> MarkRunningInterruptedAsync(DateTimeOffset startupUtc, CancellationToken cancellationToken = default) =>
        _writeQueue.ExecuteAsync(async (connection, token) =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE Runs
                SET Status = $interrupted,
                    Revision = Revision + 1,
                    CompletedAtUtc = $completed
                WHERE Status = $running;
                """;
            command.Parameters.AddWithValue("$interrupted", (int)SimulationSessionStatus.Interrupted);
            command.Parameters.AddWithValue("$running", (int)SimulationSessionStatus.Running);
            command.Parameters.AddWithValue("$completed", FormatUtc(startupUtc));
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, cancellationToken).AsTask();

    public Task<int> HealthCheckAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(async connection =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }, cancellationToken);

    public Task CreateRunAsync(
        ExperimentManifest manifest,
        SimulationMetadata metadata,
        SimulationSessionSnapshot initialSnapshot,
        RecordingSettings recording,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default) =>
        InsertRunAsync(
            manifest.ToJson(),
            metadata,
            initialSnapshot,
            recording,
            createdAtUtc,
            cancellationToken);

    public Task CreateRunFromRecordingHeaderAsync(
        RecordingHeader header,
        CancellationToken cancellationToken = default)
    {
        ExperimentManifest manifest = ExperimentManifest.FromJson(header.ExperimentManifestJson);
        if (manifest.ExperimentId != header.SimulationRunIdentity.ExperimentId ||
            header.Metadata.RunIdentity != header.SimulationRunIdentity ||
            header.Metadata.SessionId != header.SessionId ||
            header.Width != manifest.GridWidth || header.Height != manifest.GridHeight)
        {
            throw new InvalidDataException("The recording header does not match its experiment manifest or session metadata.");
        }

        SimulationSessionSnapshot snapshot = new(
            header.SessionId,
            0,
            SimulationSessionStatus.Created,
            0,
            null,
            new SimulationOperationalCounters(TimeSpan.Zero, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            null,
            RecordingState.Active,
            null);
        return InsertRunAsync(
            header.ExperimentManifestJson,
            header.Metadata,
            snapshot,
            RecordingSettings.FromHeader(header),
            header.CreatedAtUtc,
            cancellationToken);
    }

    public Task UpdateSnapshotAsync(
        Guid sessionId,
        SimulationSessionSnapshot snapshot,
        MeasurementSample? measurement = null,
        DateTimeOffset? changedAtUtc = null,
        CancellationToken cancellationToken = default) =>
        _writeQueue.ExecuteAsync(async (connection, token) =>
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);
            PersistedRun? existing = await ReadRunAsync(connection, transaction, sessionId, token).ConfigureAwait(false);
            if (existing is null)
                throw new KeyNotFoundException($"Persisted run '{sessionId:D}' was not found.");

            DateTimeOffset now = changedAtUtc ?? DateTimeOffset.UtcNow;
            StoredRunPayload payload = existing.Payload;
            if (IsTerminal(snapshot.Status))
            {
                payload = payload with
                {
                    Result = new TerminalRunSummary(
                        snapshot.Status,
                        snapshot.CurrentMcs,
                        existing.Payload.Metadata.RunIdentity,
                        snapshot.LatestMeasurement is null ? null : MetricRowMapper.ToSample(snapshot.LatestMeasurement),
                        snapshot.Failure,
                        snapshot.RecordingState,
                        snapshot.RecordingFailure)
                };
            }

            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE Runs
                    SET Status = $status,
                        Revision = $revision,
                        CurrentMcs = $mcs,
                        StartedAtUtc = CASE WHEN $status = $running AND StartedAtUtc IS NULL THEN $changed ELSE StartedAtUtc END,
                        CompletedAtUtc = CASE WHEN $terminal = 1 THEN COALESCE(CompletedAtUtc, $changed) ELSE CompletedAtUtc END,
                        Failure = $failure,
                        RecordingState = $recordingState,
                        RecordingFailure = $recordingFailure,
                        ResultJson = $resultJson
                    WHERE Id = $id;
                    """;
                command.Parameters.AddWithValue("$status", (int)snapshot.Status);
                command.Parameters.AddWithValue("$revision", snapshot.Revision);
                command.Parameters.AddWithValue("$mcs", snapshot.CurrentMcs);
                command.Parameters.AddWithValue("$running", (int)SimulationSessionStatus.Running);
                command.Parameters.AddWithValue("$terminal", IsTerminal(snapshot.Status) ? 1 : 0);
                command.Parameters.AddWithValue("$changed", FormatUtc(now));
                command.Parameters.AddWithValue("$failure", (object?)snapshot.Failure ?? DBNull.Value);
                command.Parameters.AddWithValue("$recordingState", (int)snapshot.RecordingState);
                command.Parameters.AddWithValue("$recordingFailure", (object?)snapshot.RecordingFailure ?? DBNull.Value);
                command.Parameters.AddWithValue("$resultJson", JsonSerializer.Serialize(payload, JsonOptions));
                command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            if (measurement is not null)
            {
                await UpsertMetricsAsync(
                    connection,
                    transaction,
                    sessionId,
                    measurement.Mcs,
                    MetricRowMapper.ToRows(measurement),
                    token).ConfigureAwait(false);
            }

            await transaction.CommitAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken).AsTask();

    public Task UpdateRecordingStateAsync(
        Guid sessionId,
        RecordingState state,
        string? failure,
        CancellationToken cancellationToken = default) =>
        _writeQueue.ExecuteAsync(async (connection, token) =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE Runs
                SET RecordingState = $state, RecordingFailure = $failure
                WHERE Id = $id;
                """;
            command.Parameters.AddWithValue("$state", (int)state);
            command.Parameters.AddWithValue("$failure", (object?)failure ?? DBNull.Value);
            command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
            if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                throw new KeyNotFoundException($"Persisted run '{sessionId:D}' was not found.");
            return true;
        }, cancellationToken).AsTask();

    public Task AppendFrameAsync(
        Guid sessionId,
        EncodedRecordingFrame frame,
        CancellationToken cancellationToken = default) =>
        _writeQueue.ExecuteAsync(async (connection, token) =>
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);
            await using (SqliteCommand insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO Frames
                        (RunId, Sequence, Mcs, Kind, EnvelopeVersion, PayloadCodecVersion, Compression, Payload, UncompressedSize)
                    VALUES
                        ($runId, $sequence, $mcs, $kind, $envelopeVersion, $codecVersion, $compression, $payload, $size);
                    """;
                insert.Parameters.AddWithValue("$runId", sessionId.ToString("D"));
                insert.Parameters.AddWithValue("$sequence", frame.Sequence);
                insert.Parameters.AddWithValue("$mcs", frame.Mcs);
                insert.Parameters.AddWithValue("$kind", (int)frame.Kind);
                insert.Parameters.AddWithValue("$envelopeVersion", frame.EnvelopeVersion);
                insert.Parameters.AddWithValue("$codecVersion", frame.PayloadCodecVersion);
                insert.Parameters.AddWithValue("$compression", frame.Compression);
                insert.Parameters.AddWithValue("$payload", frame.EnvelopeBytes);
                insert.Parameters.AddWithValue("$size", frame.UncompressedPayloadSize);
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            await using (SqliteCommand update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE Runs SET RecordingBytes = RecordingBytes + $bytes WHERE Id = $runId;";
                update.Parameters.AddWithValue("$bytes", frame.EnvelopeBytes.Length);
                update.Parameters.AddWithValue("$runId", sessionId.ToString("D"));
                if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                    throw new KeyNotFoundException($"Persisted run '{sessionId:D}' was not found.");
            }

            await transaction.CommitAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken).AsTask();

    public Task CompleteRecordingAsync(
        Guid sessionId,
        RecordingCompletion completion,
        CancellationToken cancellationToken = default) =>
        _writeQueue.ExecuteAsync(async (connection, token) =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE Runs
                SET RecordingState = $state, RecordingFailure = NULL
                WHERE Id = $id;
                """;
            command.Parameters.AddWithValue("$state", (int)RecordingState.Completed);
            command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
            if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                throw new KeyNotFoundException($"Persisted run '{sessionId:D}' was not found.");
            return true;
        }, cancellationToken).AsTask();

    public Task<PersistedRun?> GetRunAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        ReadAsync(connection => ReadRunAsync(connection, null, sessionId, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<PersistedRun>> GetRunsAsync(int take = 100, CancellationToken cancellationToken = default) =>
        ReadAsync(async connection =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Id FROM Runs ORDER BY CreatedAtUtc DESC LIMIT $take;";
            command.Parameters.AddWithValue("$take", take);
            List<Guid> ids = [];
            await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    ids.Add(Guid.Parse(reader.GetString(0)));
            }

            List<PersistedRun> runs = new(ids.Count);
            foreach (Guid id in ids)
            {
                PersistedRun? run = await ReadRunAsync(connection, null, id, cancellationToken).ConfigureAwait(false);
                if (run is not null)
                    runs.Add(run);
            }
            return (IReadOnlyList<PersistedRun>)runs;
        }, cancellationToken);

    public Task<IReadOnlyList<PersistedMetricSample>> GetMetricsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        ReadAsync(async connection =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Mcs, Metric, Value FROM Metrics WHERE RunId = $id ORDER BY Mcs, Metric;";
            command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
            Dictionary<long, Dictionary<string, double>> samples = [];
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                long mcs = reader.GetInt64(0);
                if (!samples.TryGetValue(mcs, out Dictionary<string, double>? values))
                    samples.Add(mcs, values = new Dictionary<string, double>(StringComparer.Ordinal));
                values.Add(reader.GetString(1), reader.GetDouble(2));
            }

            return (IReadOnlyList<PersistedMetricSample>)samples
                .Select(pair => new PersistedMetricSample(pair.Key, pair.Value))
                .ToArray();
        }, cancellationToken);

    public async Task<PersistedMetricSample?> GetLatestMetricAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PersistedMetricSample> samples = await GetMetricsAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return samples.Count == 0 ? null : samples[^1];
    }

    public Task<IReadOnlyList<RecordingFrameIndexEntry>> GetFrameIndexAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        ReadAsync(async connection =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Sequence, Mcs, Kind FROM Frames WHERE RunId = $id ORDER BY Sequence;";
            command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
            List<RecordingFrameIndexEntry> entries = [];
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                long sequence = reader.GetInt64(0);
                long mcs = reader.GetInt64(1);
                int kindValue = reader.GetInt32(2);
                if (kindValue is < byte.MinValue or > byte.MaxValue || !Enum.IsDefined((RecordingFrameKind)kindValue))
                    throw new InvalidDataException($"Recording frame {sequence} has unsupported kind {kindValue}.");
                entries.Add(new RecordingFrameIndexEntry(sequence, mcs, (RecordingFrameKind)kindValue));
            }

            for (int index = 0; index < entries.Count; index++)
            {
                if (entries[index].Sequence != index || entries[index].Mcs < 0 ||
                    (index > 0 && entries[index].Mcs < entries[index - 1].Mcs))
                {
                    throw new InvalidDataException("The stored recording index is not a valid increasing sequence.");
                }
            }

            if (entries.Count > 0 &&
                (entries[0].Sequence != 0 || entries[0].Mcs != 0 || entries[0].Kind != RecordingFrameKind.Keyframe))
            {
                throw new InvalidDataException("A recording must start with an MCS 0 keyframe at sequence zero.");
            }

            return (IReadOnlyList<RecordingFrameIndexEntry>)entries;
        }, cancellationToken);

    public async Task<EncodedRecordingFrame> ReadFrameAsync(
        Guid sessionId,
        long sequence,
        CancellationToken cancellationToken = default)
    {
        PersistedRun run = await GetRunAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Persisted run '{sessionId:D}' was not found.");
        if (!run.Payload.Recording.Enabled)
            throw new InvalidOperationException($"Persisted run '{sessionId:D}' has recording disabled.");

        await using SqliteConnection connection = await OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Sequence, Mcs, Kind, EnvelopeVersion, PayloadCodecVersion, Compression, Payload, UncompressedSize
            FROM Frames WHERE RunId = $id AND Sequence = $sequence;
            """;
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$sequence", sequence);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new KeyNotFoundException($"Recording frame {sequence} for run '{sessionId:D}' was not found.");

        int kindValue = reader.GetInt32(2);
        if (kindValue is < byte.MinValue or > byte.MaxValue || !Enum.IsDefined((RecordingFrameKind)kindValue))
            throw new InvalidDataException($"Recording frame {sequence} has unsupported kind {kindValue}.");
        RecordingFrameKind kind = (RecordingFrameKind)kindValue;
        int envelopeVersion = reader.GetInt32(3);
        int codecVersion = reader.GetInt32(4);
        string compression = reader.GetString(5);
        byte[] envelopeBytes = (byte[])reader[6];
        int uncompressedSize = reader.GetInt32(7);
        long mcs = reader.GetInt64(1);
        int expectedCodecVersion = kind == RecordingFrameKind.Keyframe
            ? run.Payload.Recording.KeyframeCodecVersion
            : run.Payload.Recording.DeltaCodecVersion;
        if (envelopeVersion != run.Payload.Recording.EnvelopeVersion ||
            codecVersion != expectedCodecVersion || compression != run.Payload.Recording.Compression ||
            mcs < 0 || envelopeBytes.Length == 0 || uncompressedSize < 0)
        {
            throw new InvalidDataException($"Recording frame {sequence} metadata differs from the recording header.");
        }

        RecordedFrameEnvelope envelope;
        try
        {
            envelope = RecordingMessagePackCodec.Deserialize(envelopeBytes);
        }
        catch (Exception exception) when (exception is MessagePack.MessagePackSerializationException or FormatException or ArgumentException)
        {
            throw new InvalidDataException($"Recording frame {sequence} has an invalid envelope.", exception);
        }

        if (envelope.EnvelopeVersion != envelopeVersion || envelope.Sequence != sequence || envelope.Mcs != mcs ||
            envelope.Kind != kind || envelope.PayloadCodecVersion != codecVersion || envelope.Compression != compression ||
            envelope.Width != run.Width || envelope.Height != run.Height ||
            envelope.UncompressedPayloadSize != uncompressedSize || envelope.Payload.Length != uncompressedSize)
        {
            throw new InvalidDataException($"Recording frame {sequence} envelope differs from its row or run metadata.");
        }

        return new EncodedRecordingFrame(
            sequence,
            mcs,
            kind,
            envelopeVersion,
            codecVersion,
            compression,
            envelopeBytes,
            uncompressedSize);
    }

    public Task<RecordingFrameIndexEntry?> FindNearestKeyframeAtOrBeforeAsync(
        Guid sessionId,
        long mcs,
        CancellationToken cancellationToken = default) =>
        ReadAsync(async connection =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT Sequence, Mcs, Kind FROM Frames
                WHERE RunId = $id AND Kind = $keyframe AND Mcs <= $mcs
                ORDER BY Mcs DESC, Sequence DESC LIMIT 1;
                """;
            command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
            command.Parameters.AddWithValue("$keyframe", (int)RecordingFrameKind.Keyframe);
            command.Parameters.AddWithValue("$mcs", mcs);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? new RecordingFrameIndexEntry(reader.GetInt64(0), reader.GetInt64(1), RecordingFrameKind.Keyframe)
                : null;
        }, cancellationToken);

    internal async ValueTask<SqliteConnection> OpenReadConnectionAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(_readConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task<int> GetUserVersionAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(ReadUserVersionAsync, cancellationToken);

    private Task InsertRunAsync(
        string manifestJson,
        SimulationMetadata metadata,
        SimulationSessionSnapshot snapshot,
        RecordingSettings recording,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken) =>
        _writeQueue.ExecuteAsync(async (connection, token) =>
        {
            StoredRunPayload payload = StoredRunPayload.Create(metadata, recording);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Runs
                    (Id, ExperimentId, ReplicateId, ReplicateIndex, Status, Revision, Width, Height,
                     CreatedAtUtc, StartedAtUtc, CompletedAtUtc, CurrentMcs, KernelId, ManifestJson,
                     ResultJson, Failure, RecordingState, RecordingFailure, RecordingBytes)
                VALUES
                    ($id, $experiment, $replicate, $replicateIndex, $status, $revision, $width, $height,
                     $created, NULL, NULL, $mcs, $kernel, $manifest, $result, NULL, $recordingState, NULL, 0);
                """;
            command.Parameters.AddWithValue("$id", metadata.SessionId.ToString("D"));
            command.Parameters.AddWithValue("$experiment", metadata.RunIdentity.ExperimentId);
            command.Parameters.AddWithValue("$replicate", metadata.RunIdentity.ReplicateId);
            command.Parameters.AddWithValue("$replicateIndex", metadata.RunIdentity.ReplicateIndex);
            command.Parameters.AddWithValue("$status", (int)snapshot.Status);
            command.Parameters.AddWithValue("$revision", snapshot.Revision);
            command.Parameters.AddWithValue("$width", metadata.GridWidth);
            command.Parameters.AddWithValue("$height", metadata.GridHeight);
            command.Parameters.AddWithValue("$created", FormatUtc(createdAtUtc));
            command.Parameters.AddWithValue("$mcs", snapshot.CurrentMcs);
            command.Parameters.AddWithValue("$kernel", metadata.RunIdentity.KernelId);
            command.Parameters.AddWithValue("$manifest", manifestJson);
            command.Parameters.AddWithValue("$result", JsonSerializer.Serialize(payload, JsonOptions));
            command.Parameters.AddWithValue("$recordingState", (int)snapshot.RecordingState);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);

            if (snapshot.LatestMeasurement is not null)
                await UpsertMetricsAsync(connection, transaction, metadata.SessionId, snapshot.LatestMeasurement.Mcs,
                    MetricRowMapper.ToRows(snapshot.LatestMeasurement), token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken).AsTask();

    private async Task<T> ReadAsync<T>(
        Func<SqliteConnection, Task<T>> query,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await query(connection).ConfigureAwait(false);
    }

    private async Task<T> ReadAsync<T>(
        Func<SqliteConnection, CancellationToken, Task<T>> query,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await query(connection, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PersistedRun?> ReadRunAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id, ExperimentId, ReplicateId, ReplicateIndex, Status, Revision, Width, Height,
                   CreatedAtUtc, StartedAtUtc, CompletedAtUtc, CurrentMcs, KernelId, ManifestJson,
                   ResultJson, Failure, RecordingState, RecordingFailure, RecordingBytes
            FROM Runs WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        string? resultJson = reader.IsDBNull(14) ? null : reader.GetString(14);
        StoredRunPayload payload = resultJson is null
            ? throw new InvalidDataException($"Persisted run '{sessionId:D}' is missing its run metadata.")
            : JsonSerializer.Deserialize<StoredRunPayload>(resultJson, JsonOptions)
                ?? throw new InvalidDataException($"Persisted run '{sessionId:D}' has invalid run metadata.");
        return new PersistedRun(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            (SimulationSessionStatus)reader.GetInt32(4),
            reader.GetInt64(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            ParseUtc(reader.GetString(8)),
            reader.IsDBNull(9) ? null : ParseUtc(reader.GetString(9)),
            reader.IsDBNull(10) ? null : ParseUtc(reader.GetString(10)),
            reader.GetInt64(11),
            reader.GetString(12),
            reader.GetString(13),
            resultJson,
            reader.IsDBNull(15) ? null : reader.GetString(15),
            (RecordingState)reader.GetInt32(16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.GetInt64(18),
            payload);
    }

    private static async ValueTask UpsertMetricsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId,
        long mcs,
        IReadOnlyList<MetricRow> metrics,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Metrics (RunId, Mcs, Metric, Value)
            VALUES ($runId, $mcs, $metric, $value)
            ON CONFLICT (RunId, Mcs, Metric) DO UPDATE SET Value = excluded.Value;
            """;
        SqliteParameter runId = command.Parameters.Add("$runId", SqliteType.Text);
        SqliteParameter sampleMcs = command.Parameters.Add("$mcs", SqliteType.Integer);
        SqliteParameter metric = command.Parameters.Add("$metric", SqliteType.Text);
        SqliteParameter value = command.Parameters.Add("$value", SqliteType.Real);
        runId.Value = sessionId.ToString("D");
        sampleMcs.Value = mcs;
        foreach (MetricRow row in metrics)
        {
            metric.Value = row.Metric;
            value.Value = row.Value;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<int> ReadUserVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        object? version = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(version, CultureInfo.InvariantCulture);
    }

    private static bool IsTerminal(SimulationSessionStatus status) => status is
        SimulationSessionStatus.Completed or SimulationSessionStatus.Failed or
        SimulationSessionStatus.Interrupted or SimulationSessionStatus.Cancelled;

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private const string SchemaSql = """
        CREATE TABLE Runs (
            Id TEXT NOT NULL PRIMARY KEY,
            ExperimentId TEXT NOT NULL,
            ReplicateId TEXT NOT NULL,
            ReplicateIndex INTEGER NOT NULL,
            Status INTEGER NOT NULL,
            Revision INTEGER NOT NULL,
            Width INTEGER NOT NULL,
            Height INTEGER NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            StartedAtUtc TEXT NULL,
            CompletedAtUtc TEXT NULL,
            CurrentMcs INTEGER NOT NULL,
            KernelId TEXT NOT NULL,
            ManifestJson TEXT NOT NULL,
            ResultJson TEXT NULL,
            Failure TEXT NULL,
            RecordingState INTEGER NOT NULL,
            RecordingFailure TEXT NULL,
            RecordingBytes INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE Frames (
            RunId TEXT NOT NULL,
            Sequence INTEGER NOT NULL,
            Mcs INTEGER NOT NULL,
            Kind INTEGER NOT NULL,
            EnvelopeVersion INTEGER NOT NULL,
            PayloadCodecVersion INTEGER NOT NULL,
            Compression TEXT NOT NULL,
            Payload BLOB NOT NULL,
            UncompressedSize INTEGER NOT NULL,
            PRIMARY KEY (RunId, Sequence),
            FOREIGN KEY (RunId) REFERENCES Runs(Id) ON DELETE CASCADE
        );

        CREATE INDEX IX_Frames_RunId_Mcs
        ON Frames(RunId, Mcs);

        CREATE TABLE Metrics (
            RunId TEXT NOT NULL,
            Mcs INTEGER NOT NULL,
            Metric TEXT NOT NULL,
            Value REAL NOT NULL,
            PRIMARY KEY (RunId, Mcs, Metric),
            FOREIGN KEY (RunId) REFERENCES Runs(Id) ON DELETE CASCADE
        );
        """;
}

public sealed class UnsupportedDatabaseSchemaException(int foundVersion, int expectedVersion)
    : InvalidOperationException($"Database schema version {foundVersion} is unsupported; expected {expectedVersion}.");
