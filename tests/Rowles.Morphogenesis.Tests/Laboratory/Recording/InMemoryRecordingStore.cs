using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Laboratory.Playback;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;

namespace Rowles.Morphogenesis.Tests.Laboratory.Recording;

internal sealed class InMemoryRecordingStore : IRecordingStore, IRecordingReader
{
    private readonly object _gate = new();
    private RecordingHeader? _header;
    private List<EncodedRecordingFrame> _frames = [];
    private RecordingCompletion? _completion;

    internal Func<CancellationToken, ValueTask>? CreateBehavior { get; set; }

    internal Func<EncodedRecordingFrame, CancellationToken, ValueTask>? AppendBehavior { get; set; }

    internal Func<CancellationToken, ValueTask>? CompleteBehavior { get; set; }

    public async ValueTask CreateAsync(RecordingHeader header, CancellationToken cancellationToken)
    {
        if (CreateBehavior is not null)
        {
            await CreateBehavior(cancellationToken).ConfigureAwait(false);
        }

        lock (_gate)
        {
            _header = header;
            _frames = [];
            _completion = null;
        }
    }

    public async ValueTask AppendFrameAsync(Guid sessionId, EncodedRecordingFrame frame, CancellationToken cancellationToken)
    {
        if (AppendBehavior is not null)
        {
            await AppendBehavior(frame, cancellationToken).ConfigureAwait(false);
        }

        lock (_gate)
        {
            if (_header?.SessionId != sessionId)
            {
                throw new InvalidOperationException("The session has no recording header.");
            }

            _frames.Add(Clone(frame));
        }
    }

    public async ValueTask CompleteAsync(Guid sessionId, RecordingCompletion completion, CancellationToken cancellationToken)
    {
        if (CompleteBehavior is not null)
        {
            await CompleteBehavior(cancellationToken).ConfigureAwait(false);
        }

        lock (_gate)
        {
            if (_header?.SessionId != sessionId)
            {
                throw new InvalidOperationException("The session has no recording header.");
            }

            _completion = completion;
        }
    }

    public ValueTask<RecordingHeader> GetHeaderAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(_header?.SessionId == sessionId
                ? _header
                : throw new KeyNotFoundException("The recording header was not found."));
        }
    }

    public ValueTask<IReadOnlyList<RecordingFrameIndexEntry>> GetFrameIndexAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            EnsureSession(sessionId);
            IReadOnlyList<RecordingFrameIndexEntry> index = _frames
                .Select(frame => new RecordingFrameIndexEntry(frame.Sequence, frame.Mcs, frame.Kind))
                .ToArray();
            return ValueTask.FromResult(index);
        }
    }

    public ValueTask<EncodedRecordingFrame> ReadFrameAsync(Guid sessionId, long sequence, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            EnsureSession(sessionId);
            EncodedRecordingFrame frame = _frames.SingleOrDefault(candidate => candidate.Sequence == sequence)
                ?? throw new KeyNotFoundException($"Recording frame {sequence} was not found.");
            return ValueTask.FromResult(Clone(frame));
        }
    }

    public ValueTask<RecordingFrameIndexEntry?> FindNearestKeyframeAtOrBeforeAsync(
        Guid sessionId,
        long mcs,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            EnsureSession(sessionId);
            RecordingFrameIndexEntry? keyframe = _frames
                .Where(frame => frame.Kind == RecordingFrameKind.Keyframe && frame.Mcs <= mcs)
                .Select(frame => new RecordingFrameIndexEntry(frame.Sequence, frame.Mcs, frame.Kind))
                .LastOrDefault();
            return ValueTask.FromResult(keyframe);
        }
    }

    internal EncodedRecordingFrame[] GetStoredFrames()
    {
        lock (_gate)
        {
            return _frames.Select(Clone).ToArray();
        }
    }

    internal RecordingCompletion? GetCompletion()
    {
        lock (_gate)
        {
            return _completion;
        }
    }

    internal void Seed(RecordingHeader header, params EncodedRecordingFrame[] frames)
    {
        lock (_gate)
        {
            _header = header;
            _frames = frames.Select(Clone).ToList();
            _completion = new RecordingCompletion(DateTimeOffset.UtcNow, frames[^1].Mcs, frames.Length);
        }
    }

    private void EnsureSession(Guid sessionId)
    {
        if (_header?.SessionId != sessionId)
        {
            throw new KeyNotFoundException("The recording header was not found.");
        }
    }

    private static EncodedRecordingFrame Clone(EncodedRecordingFrame frame) => frame with
    {
        EnvelopeBytes = (byte[])frame.EnvelopeBytes.Clone()
    };
}

internal static class RecordingTestFixture
{
    internal static RecordingHeader CreateHeader(
        Guid sessionId,
        ExperimentManifest manifest,
        SimulationMetadata metadata,
        int recordEveryMcs = 5) => new(
            RecordingFormat.SchemaVersion,
            sessionId,
            metadata.RunIdentity,
            manifest.ToJson(),
            DateTimeOffset.UtcNow,
            manifest.GridWidth,
            manifest.GridHeight,
            recordEveryMcs,
            20,
            0.75,
            RecordingFormat.EnvelopeVersion,
            RecordingFormat.KeyframeCodecVersion,
            RecordingFormat.DeltaCodecVersion,
            RecordingFormat.Compression,
            metadata);

    internal static EncodedRecordingFrame EncodeFrame(
        int width,
        int height,
        long sequence,
        long mcs,
        RecordingFrameKind kind,
        int[] lattice,
        int[]? sortedIndices = null,
        int[]? changedCellIds = null)
    {
        int siteCount = checked(width * height);
        byte[] rawPayload;
        int payloadVersion;
        if (kind == RecordingFrameKind.Keyframe)
        {
            rawPayload = new byte[KeyframePayloadCodec.GetPayloadSize(siteCount)];
            KeyframePayloadCodec.Encode(lattice, rawPayload);
            payloadVersion = RecordingFormat.KeyframeCodecVersion;
        }
        else
        {
            byte[] scratch = new byte[DeltaPayloadCodec.GetBufferSize(siteCount)];
            int length = DeltaPayloadCodec.Encode(sortedIndices ?? [], changedCellIds ?? [], siteCount, scratch);
            rawPayload = scratch.AsSpan(0, length).ToArray();
            payloadVersion = RecordingFormat.DeltaCodecVersion;
        }

        RecordedFrameEnvelope envelope = new(
            RecordingFormat.EnvelopeVersion,
            sequence,
            mcs,
            kind,
            payloadVersion,
            RecordingFormat.Compression,
            width,
            height,
            rawPayload.Length,
            rawPayload);
        using PooledEnvelopeBuffer encoded = new RecordingMessagePackCodec(siteCount).Serialize(envelope);
        byte[] envelopeBytes = (byte[])encoded.Bytes.Clone();
        return new EncodedRecordingFrame(
            sequence,
            mcs,
            kind,
            RecordingFormat.EnvelopeVersion,
            payloadVersion,
            RecordingFormat.Compression,
            envelopeBytes,
            rawPayload.Length);
    }
}
