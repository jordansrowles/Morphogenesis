using System.Collections.Concurrent;
using System.Collections.Generic;
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

    internal int PendingRunCreationCount => _created.Count;

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

    internal async Task WaitForRunCreationAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource completion = _created.GetOrAdd(
            sessionId,
            static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        try
        {
            await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ((ICollection<KeyValuePair<Guid, TaskCompletionSource>>)_created)
                .Remove(new KeyValuePair<Guid, TaskCompletionSource>(sessionId, completion));
        }
    }
}
