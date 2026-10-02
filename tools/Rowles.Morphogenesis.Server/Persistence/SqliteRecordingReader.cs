using Rowles.Morphogenesis.Laboratory.Recording;

namespace Rowles.Morphogenesis.Server.Persistence;

public sealed class SqliteRecordingReader : IRecordingReader
{
    private readonly LaboratoryDatabase _database;

    public SqliteRecordingReader(LaboratoryDatabase database)
    {
        _database = database;
    }

    public async ValueTask<RecordingHeader> GetHeaderAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        PersistedRun run = await _database.GetRunAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Persisted run '{sessionId:D}' was not found.");
        RecordingSettings settings = run.Payload.Recording;
        if (!settings.Enabled)
            throw new InvalidOperationException($"Persisted run '{sessionId:D}' has recording disabled.");

        RecordingHeader header = settings.ToHeader(
            run.Id,
            run.Payload.Metadata.RunIdentity,
            run.ManifestJson,
            run.CreatedAtUtc,
            run.Width,
            run.Height,
            run.Payload.Metadata.ToMetadata());
        if (header.RecordingSchemaVersion != RecordingFormat.SchemaVersion ||
            header.SessionId != sessionId || header.Metadata.SessionId != sessionId ||
            header.Metadata.RunIdentity != header.SimulationRunIdentity ||
            header.Width != header.Metadata.GridWidth || header.Height != header.Metadata.GridHeight ||
            header.Compression != RecordingFormat.Compression)
        {
            throw new InvalidDataException($"Persisted recording header for run '{sessionId:D}' is inconsistent.");
        }

        return header;
    }

    public ValueTask<IReadOnlyList<RecordingFrameIndexEntry>> GetFrameIndexAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        new(_database.GetFrameIndexAsync(sessionId, cancellationToken));

    public ValueTask<EncodedRecordingFrame> ReadFrameAsync(
        Guid sessionId,
        long sequence,
        CancellationToken cancellationToken = default) =>
        new(_database.ReadFrameAsync(sessionId, sequence, cancellationToken));

    public ValueTask<RecordingFrameIndexEntry?> FindNearestKeyframeAtOrBeforeAsync(
        Guid sessionId,
        long mcs,
        CancellationToken cancellationToken = default) =>
        new(_database.FindNearestKeyframeAtOrBeforeAsync(sessionId, mcs, cancellationToken));
}
