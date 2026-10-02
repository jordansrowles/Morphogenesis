using System.Threading.Channels;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Laboratory.Recording;

internal sealed class SimulationRecorder : IAsyncDisposable
{
    private readonly object _completionGate = new();
    private readonly Guid _sessionId;
    private readonly MorphogenesisState _state;
    private readonly CoalescingLatticeChangeAccumulator _accumulator;
    private readonly RecordingOptions _options;
    private readonly IRecordingStore _store;
    private readonly RecordingHeader _header;
    private readonly Channel<QueuedFrame> _queue;
    private readonly CancellationTokenSource _writerCancellation = new();
    private readonly Task _writerTask;
    private readonly int[] _keyframeCellIds;
    private readonly byte[] _keyframePayload;
    private readonly byte[] _deltaPayload;
    private readonly int _keyframePayloadSize;
    private readonly RecordingMessagePackCodec _messagePackCodec;
    private readonly Action<RecordingState, string?> _stateChanged;
    private RecordingCompletion? _completion;
    private Task? _completionTask;
    private int _recordingState = (int)RecordingState.Active;
    private string? _failure;
    private long _nextSequence;
    private long _frameCount;
    private long _latestQueuedMcs = -1;
    private RecordingFrameKind _latestQueuedKind;

    internal SimulationRecorder(
        Guid sessionId,
        ExperimentManifest manifest,
        SimulationMetadata metadata,
        MorphogenesisState state,
        CoalescingLatticeChangeAccumulator accumulator,
        RecordingOptions options,
        IRecordingStore store,
        Action<RecordingState, string?> stateChanged)
    {
        _sessionId = sessionId;
        _state = state;
        _accumulator = accumulator;
        _options = options;
        _store = store;
        _stateChanged = stateChanged;
        _keyframeCellIds = new int[state.SiteCount];
        _keyframePayloadSize = KeyframePayloadCodec.GetPayloadSize(state.SiteCount);
        _keyframePayload = new byte[_keyframePayloadSize];
        _deltaPayload = new byte[DeltaPayloadCodec.GetBufferSize(state.SiteCount)];
        _messagePackCodec = new RecordingMessagePackCodec(state.SiteCount);
        _header = new RecordingHeader(
            RecordingFormat.SchemaVersion,
            sessionId,
            metadata.RunIdentity,
            manifest.ToJson(),
            DateTimeOffset.UtcNow,
            state.Width,
            state.Height,
            options.RecordEveryMcs,
            options.KeyframeEveryRecordedFrames,
            options.DeltaPromotionRatio,
            RecordingFormat.EnvelopeVersion,
            RecordingFormat.KeyframeCodecVersion,
            RecordingFormat.DeltaCodecVersion,
            RecordingFormat.Compression,
            metadata);
        _queue = Channel.CreateBounded<QueuedFrame>(new BoundedChannelOptions(options.WriterQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        _writerTask = Task.Run(WriterLoopAsync);
        try
        {
            CaptureKeyframe(0);
        }
        catch (Exception exception)
        {
            MarkFailed(exception);
        }
    }

    internal RecordingState State => (RecordingState)Volatile.Read(ref _recordingState);

    internal string? Failure => Volatile.Read(ref _failure);

    internal long FrameCount => Volatile.Read(ref _frameCount);

    internal void RecordCompletedMcs(long mcs)
    {
        if (State != RecordingState.Active || mcs <= 0 || mcs % _options.RecordEveryMcs != 0)
        {
            return;
        }

        try
        {
            long frameOrdinal = _frameCount + 1;
            if (frameOrdinal % _options.KeyframeEveryRecordedFrames == 0)
            {
                CaptureKeyframe(mcs);
                return;
            }

            _accumulator.SortChanges();
            int deltaPayloadSize = DeltaPayloadCodec.Encode(
                _accumulator.SortedIndices,
                _accumulator.SortedCellIds,
                _state.SiteCount,
                _deltaPayload);
            if (ShouldPromoteDelta(deltaPayloadSize, _keyframePayloadSize))
            {
                CaptureKeyframe(mcs);
                return;
            }

            EnqueueFrame(mcs, RecordingFrameKind.Delta, _deltaPayload, deltaPayloadSize);
        }
        catch (Exception exception)
        {
            MarkFailed(exception);
        }
    }

    internal Task CompleteAsync(long finalMcs)
    {
        lock (_completionGate)
        {
            return _completionTask ??= CompleteCoreAsync(finalMcs);
        }
    }

    internal static bool ShouldPromoteDelta(int deltaPayloadSize, int keyframePayloadSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deltaPayloadSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(keyframePayloadSize);
        return (long)deltaPayloadSize * 4 >= (long)keyframePayloadSize * 3;
    }

    public async ValueTask DisposeAsync()
    {
        await CompleteAsync(_latestQueuedMcs < 0 ? 0 : _latestQueuedMcs).ConfigureAwait(false);
        _writerCancellation.Dispose();
    }

    private async Task CompleteCoreAsync(long finalMcs)
    {
        if (State == RecordingState.Active)
        {
            try
            {
                if (_latestQueuedKind != RecordingFrameKind.Keyframe || _latestQueuedMcs != finalMcs)
                {
                    CaptureKeyframe(finalMcs);
                }

                if (State == RecordingState.Active)
                {
                    _completion = new RecordingCompletion(DateTimeOffset.UtcNow, finalMcs, _frameCount);
                    _queue.Writer.TryComplete();
                }
            }
            catch (Exception exception)
            {
                MarkFailed(exception);
            }
        }

        await _writerTask.ConfigureAwait(false);
    }

    private void CaptureKeyframe(long mcs)
    {
        _state.CopyCellIdsTo(_keyframeCellIds);
        int payloadSize = KeyframePayloadCodec.Encode(_keyframeCellIds, _keyframePayload);
        EnqueueFrame(mcs, RecordingFrameKind.Keyframe, _keyframePayload, payloadSize);
    }

    private void EnqueueFrame(long mcs, RecordingFrameKind kind, byte[] payloadBuffer, int payloadSize)
    {
        if (State != RecordingState.Active)
        {
            _accumulator.Reset();
            return;
        }

        byte[]? rentedPayload = null;
        PooledEnvelopeBuffer? envelopeBuffer = null;
        try
        {
            byte[] payload;
            if (payloadSize == payloadBuffer.Length)
            {
                payload = payloadBuffer;
            }
            else
            {
                rentedPayload = ExactByteArrayPool.Instance.Rent(payloadSize);
                payloadBuffer.AsSpan(0, payloadSize).CopyTo(rentedPayload);
                payload = rentedPayload;
            }

            int payloadCodecVersion = kind == RecordingFrameKind.Keyframe
                ? RecordingFormat.KeyframeCodecVersion
                : RecordingFormat.DeltaCodecVersion;
            RecordedFrameEnvelope envelope = new(
                RecordingFormat.EnvelopeVersion,
                _nextSequence,
                mcs,
                kind,
                payloadCodecVersion,
                RecordingFormat.Compression,
                _state.Width,
                _state.Height,
                payloadSize,
                payload);
            envelopeBuffer = _messagePackCodec.Serialize(envelope);
            EncodedRecordingFrame frame = new(
                _nextSequence,
                mcs,
                kind,
                RecordingFormat.EnvelopeVersion,
                payloadCodecVersion,
                RecordingFormat.Compression,
                envelopeBuffer.Bytes,
                payloadSize);
            QueuedFrame queued = new(frame, envelopeBuffer);
            if (!_queue.Writer.TryWrite(queued))
            {
                envelopeBuffer.Dispose();
                envelopeBuffer = null;
                MarkFailed(new RecordingBackpressureException());
                return;
            }

            envelopeBuffer = null;
            _latestQueuedMcs = mcs;
            _latestQueuedKind = kind;
            _nextSequence++;
            _frameCount++;
            _accumulator.Reset();
        }
        finally
        {
            envelopeBuffer?.Dispose();
            if (rentedPayload is not null)
            {
                ExactByteArrayPool.Instance.Return(rentedPayload);
            }
        }
    }

    private async Task WriterLoopAsync()
    {
        CancellationToken cancellationToken = _writerCancellation.Token;
        try
        {
            await _store.CreateAsync(_header, cancellationToken).ConfigureAwait(false);
            while (await _queue.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out QueuedFrame? queued))
                {
                    using (queued.Buffer)
                    {
                        await _store.AppendFrameAsync(_sessionId, queued.Frame, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            RecordingCompletion completion = _completion
                ?? throw new InvalidOperationException("The recorder queue was closed without a completion record.");
            await _store.CompleteAsync(_sessionId, completion, cancellationToken).ConfigureAwait(false);
            SetState(RecordingState.Completed, null);
        }
        catch (Exception exception)
        {
            if (State == RecordingState.Active)
            {
                MarkFailed(exception);
            }

            DrainQueue();
        }
    }

    private void DrainQueue()
    {
        while (_queue.Reader.TryRead(out QueuedFrame? queued))
        {
            queued.Buffer.Dispose();
        }
    }

    private void MarkFailed(Exception exception)
    {
        string failure = DescribeFailure(exception);
        if (Interlocked.CompareExchange(ref _recordingState, (int)RecordingState.Failed, (int)RecordingState.Active)
            != (int)RecordingState.Active)
        {
            return;
        }

        Volatile.Write(ref _failure, failure);
        _queue.Writer.TryComplete(exception);
        _writerCancellation.Cancel();
        NotifyState(RecordingState.Failed, failure);
    }

    private static string DescribeFailure(Exception exception)
    {
        string failure = $"{exception.GetType().Name}: {exception.Message}";
        if (exception.InnerException is not null)
        {
            failure += $" ({exception.InnerException.GetType().Name}: {exception.InnerException.Message})";
        }

        return failure;
    }

    private void SetState(RecordingState state, string? failure)
    {
        if (Interlocked.CompareExchange(ref _recordingState, (int)state, (int)RecordingState.Active)
            == (int)RecordingState.Active)
        {
            Volatile.Write(ref _failure, failure);
            NotifyState(state, failure);
        }
    }

    private void NotifyState(RecordingState state, string? failure)
    {
        try
        {
            _stateChanged(state, failure);
        }
        catch
        {
        }
    }

    private sealed record QueuedFrame(EncodedRecordingFrame Frame, PooledEnvelopeBuffer Buffer);
}
