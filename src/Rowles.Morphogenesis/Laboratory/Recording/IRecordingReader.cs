namespace Rowles.Morphogenesis.Laboratory.Recording;

public interface IRecordingReader
{
    ValueTask<RecordingHeader> GetHeaderAsync(Guid sessionId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<RecordingFrameIndexEntry>> GetFrameIndexAsync(Guid sessionId, CancellationToken cancellationToken = default);

    ValueTask<EncodedRecordingFrame> ReadFrameAsync(Guid sessionId, long sequence, CancellationToken cancellationToken = default);

    ValueTask<RecordingFrameIndexEntry?> FindNearestKeyframeAtOrBeforeAsync(Guid sessionId, long mcs, CancellationToken cancellationToken = default);
}
