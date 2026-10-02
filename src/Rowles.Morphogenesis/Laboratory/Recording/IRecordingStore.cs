namespace Rowles.Morphogenesis.Laboratory.Recording;

public interface IRecordingStore
{
    ValueTask CreateAsync(RecordingHeader header, CancellationToken cancellationToken);

    ValueTask AppendFrameAsync(Guid sessionId, EncodedRecordingFrame frame, CancellationToken cancellationToken);

    ValueTask CompleteAsync(Guid sessionId, RecordingCompletion completion, CancellationToken cancellationToken);
}
