using System.Collections.Concurrent;
using Rowles.Morphogenesis.Laboratory.Recording;

namespace Rowles.Morphogenesis.Server.Persistence;

public sealed class SqliteRecordingStore : IRecordingStore
{
    private readonly LaboratoryDatabase _database;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _created = new();

    public SqliteRecordingStore(LaboratoryDatabase database)
    {
        _database = database;
    }

    public async ValueTask CreateAsync(RecordingHeader header, CancellationToken cancellationToken)
    {
        TaskCompletionSource completion = _created.GetOrAdd(
            header.SessionId,
            static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        try
        {
            await _database.CreateRunFromRecordingHeaderAsync(header, cancellationToken).ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
            throw;
        }
    }

    public ValueTask AppendFrameAsync(
        Guid sessionId,
        EncodedRecordingFrame frame,
        CancellationToken cancellationToken) =>
        new(_database.AppendFrameAsync(sessionId, frame, cancellationToken));

    public ValueTask CompleteAsync(
        Guid sessionId,
        RecordingCompletion completion,
        CancellationToken cancellationToken) =>
        new(_database.CompleteRecordingAsync(sessionId, completion, cancellationToken));

    internal Task WaitForRunCreationAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource completion = _created.GetOrAdd(
            sessionId,
            static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        return completion.Task.WaitAsync(cancellationToken);
    }
}
