using System.Buffers;
using Rowles.Morphogenesis.Laboratory.Recording;

namespace Rowles.Morphogenesis.Laboratory.Playback;

public sealed class RecordingFrameReconstructor
{
    private readonly SemaphoreSlim _workspaceSlots;
    private readonly ArrayPool<int> _arrayPool;
    private readonly int _maximumSiteCount;

    public RecordingFrameReconstructor(int maximumConcurrentReconstructions, int maximumSiteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumConcurrentReconstructions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSiteCount);
        _workspaceSlots = new SemaphoreSlim(maximumConcurrentReconstructions, maximumConcurrentReconstructions);
        _arrayPool = ArrayPool<int>.Create(maximumSiteCount, checked(maximumConcurrentReconstructions * 3));
        _maximumSiteCount = maximumSiteCount;
    }

    public async ValueTask<ReconstructedRecordingFrame> ReconstructAsync(
        IRecordingReader reader,
        Guid sessionId,
        long mcs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegative(mcs);
        await _workspaceSlots.WaitAsync(cancellationToken).ConfigureAwait(false);

        int[]? lattice = null;
        int[]? deltaIndices = null;
        int[]? deltaCellIds = null;
        bool transferred = false;
        try
        {
            RecordingHeader header = await reader.GetHeaderAsync(sessionId, cancellationToken).ConfigureAwait(false);
            RecordedSimulationSource.ValidateHeader(header, sessionId);
            int siteCount;
            try
            {
                siteCount = checked(header.Width * header.Height);
            }
            catch (OverflowException exception)
            {
                throw new FormatException("The recording lattice dimensions overflow the supported site-count range.", exception);
            }

            if (siteCount > _maximumSiteCount)
                throw new InvalidDataException("The recording lattice exceeds the configured frame-reconstruction limit.");

            lattice = _arrayPool.Rent(siteCount);
            deltaIndices = _arrayPool.Rent(siteCount);
            deltaCellIds = _arrayPool.Rent(siteCount);

            RecordingFrameIndexEntry target = await reader.FindFrameAtOrBeforeAsync(sessionId, mcs, cancellationToken).ConfigureAwait(false)
                ?? throw new ArgumentOutOfRangeException(nameof(mcs), "No recorded frame exists at or before the requested MCS.");
            RecordingFrameIndexEntry keyframe = await reader.FindNearestKeyframeAtOrBeforeAsync(
                sessionId,
                target.Mcs,
                cancellationToken).ConfigureAwait(false)
                ?? throw new FormatException("The recording has no keyframe at or before the requested MCS.");
            if (keyframe.Kind != RecordingFrameKind.Keyframe || keyframe.Sequence > target.Sequence ||
                target.Sequence - keyframe.Sequence >= header.KeyframeEveryRecordedFrames)
            {
                throw new FormatException("The recording reader returned an invalid keyframe interval for the target frame.");
            }

            long previousMcs = -1;
            for (long sequence = keyframe.Sequence; sequence <= target.Sequence; sequence++)
            {
                EncodedRecordingFrame frame = await reader.ReadFrameAsync(sessionId, sequence, cancellationToken).ConfigureAwait(false);
                if (frame.Sequence != sequence || frame.Mcs < previousMcs || frame.Mcs > target.Mcs)
                    throw new FormatException("The stored frame sequence is inconsistent with the requested reconstruction interval.");

                RecordingFrameIndexEntry expected = sequence == keyframe.Sequence
                    ? keyframe
                    : sequence == target.Sequence
                        ? target
                        : new RecordingFrameIndexEntry(sequence, frame.Mcs, frame.Kind);
                if (frame.Mcs != expected.Mcs || frame.Kind != expected.Kind)
                    throw new FormatException("The stored frame differs from its recording index.");
                if (sequence != keyframe.Sequence && frame.Kind == RecordingFrameKind.Keyframe)
                    throw new FormatException("The recording reader did not return the nearest keyframe for the target frame.");

                DecodeFrame(frame, header, expected, lattice, deltaIndices, deltaCellIds, siteCount);
                previousMcs = frame.Mcs;
            }

            _arrayPool.Return(deltaIndices);
            deltaIndices = null;
            _arrayPool.Return(deltaCellIds);
            deltaCellIds = null;
            ReconstructedRecordingFrame reconstructed = new(
                lattice,
                siteCount,
                header.Width,
                header.Height,
                target.Sequence,
                target.Mcs,
                _arrayPool,
                _workspaceSlots);
            lattice = null;
            transferred = true;
            return reconstructed;
        }
        finally
        {
            if (lattice is not null)
                _arrayPool.Return(lattice);
            if (deltaIndices is not null)
                _arrayPool.Return(deltaIndices);
            if (deltaCellIds is not null)
                _arrayPool.Return(deltaCellIds);
            if (!transferred)
                _workspaceSlots.Release();
        }
    }

    internal static void DecodeFrame(
        EncodedRecordingFrame frame,
        RecordingHeader header,
        RecordingFrameIndexEntry expected,
        int[] lattice,
        int[] deltaIndices,
        int[] deltaCellIds,
        int siteCount)
    {
        if (frame.Sequence != expected.Sequence || frame.Mcs != expected.Mcs || frame.Kind != expected.Kind ||
            frame.EnvelopeVersion != RecordingFormat.EnvelopeVersion ||
            frame.Compression != RecordingFormat.Compression)
        {
            throw new FormatException("The stored frame differs from its recording index or uses an unsupported envelope version.");
        }

        RecordedFrameEnvelope envelope = RecordingMessagePackCodec.Deserialize(frame.EnvelopeBytes);
        int expectedCodecVersion = expected.Kind == RecordingFrameKind.Keyframe
            ? RecordingFormat.KeyframeCodecVersion
            : RecordingFormat.DeltaCodecVersion;
        if (envelope.EnvelopeVersion != RecordingFormat.EnvelopeVersion ||
            envelope.Sequence != expected.Sequence ||
            envelope.Mcs != expected.Mcs ||
            envelope.Kind != expected.Kind ||
            envelope.PayloadCodecVersion != expectedCodecVersion ||
            envelope.Compression != RecordingFormat.Compression ||
            envelope.Width != header.Width ||
            envelope.Height != header.Height ||
            envelope.UncompressedPayloadSize != envelope.Payload.Length ||
            envelope.UncompressedPayloadSize != frame.UncompressedPayloadSize)
        {
            throw new FormatException("The recording envelope fields are inconsistent with the header or frame index.");
        }

        if (expected.Kind == RecordingFrameKind.Keyframe)
        {
            KeyframePayloadCodec.Decode(envelope.Payload, lattice.AsSpan(0, siteCount));
            for (int index = 0; index < siteCount; index++)
            {
                if (lattice[index] < 0)
                    throw new FormatException("A recorded keyframe contains a negative cell ID.");
            }

            return;
        }

        int changeCount = DeltaPayloadCodec.Decode(
            envelope.Payload,
            siteCount,
            deltaIndices.AsSpan(0, siteCount),
            deltaCellIds.AsSpan(0, siteCount));
        for (int change = 0; change < changeCount; change++)
            lattice[deltaIndices[change]] = deltaCellIds[change];
    }
}

public sealed class ReconstructedRecordingFrame : IDisposable
{
    private int[]? _lattice;
    private readonly int _siteCount;
    private readonly ArrayPool<int> _arrayPool;
    private SemaphoreSlim? _workspaceSlots;

    internal ReconstructedRecordingFrame(
        int[] lattice,
        int siteCount,
        int width,
        int height,
        long sequence,
        long mcs,
        ArrayPool<int> arrayPool,
        SemaphoreSlim workspaceSlots)
    {
        _lattice = lattice;
        _siteCount = siteCount;
        Width = width;
        Height = height;
        Sequence = sequence;
        Mcs = mcs;
        _arrayPool = arrayPool;
        _workspaceSlots = workspaceSlots;
    }

    public int Width { get; }

    public int Height { get; }

    public long Sequence { get; }

    public long Mcs { get; }

    public ReadOnlyMemory<int> CellIds =>
        (Volatile.Read(ref _lattice) ?? throw new ObjectDisposedException(nameof(ReconstructedRecordingFrame)))
        .AsMemory(0, _siteCount);

    public void Dispose()
    {
        int[]? lattice = Interlocked.Exchange(ref _lattice, null);
        if (lattice is null)
            return;

        _arrayPool.Return(lattice);
        Interlocked.Exchange(ref _workspaceSlots, null)?.Release();
    }
}
