using System.Buffers;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;

namespace Rowles.Morphogenesis.Laboratory.Recording;

[GeneratedMessagePackResolver]
internal partial class RecordingMessagePackResolver;

internal sealed class RecordingMessagePackCodec
{
    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard
        .WithCompression(MessagePackCompression.Lz4BlockArray)
        .WithResolver(CompositeResolver.Create(
            Array.Empty<IMessagePackFormatter>(),
            [RecordingMessagePackResolver.Instance, BuiltinResolver.Instance]));

    private readonly CountingBufferWriter _countingWriter;

    internal RecordingMessagePackCodec(int siteCount)
    {
        int capacity = checked(DeltaPayloadCodec.GetBufferSize(siteCount) + 65_536);
        _countingWriter = new CountingBufferWriter(capacity);
    }

    internal PooledEnvelopeBuffer Serialize(RecordedFrameEnvelope envelope)
    {
        _countingWriter.Reset();
        MessagePackSerializer.Serialize(_countingWriter, envelope, Options);
        int encodedLength = _countingWriter.WrittenCount;
        byte[] buffer = ExactByteArrayPool.Instance.Rent(encodedLength);
        try
        {
            _countingWriter.Reset();
            MessagePackSerializer.Serialize(_countingWriter, envelope, Options);
            if (_countingWriter.WrittenCount != encodedLength)
            {
                throw new InvalidOperationException("MessagePack envelope size changed between the counting and encoding passes.");
            }

            _countingWriter.WrittenSpan.CopyTo(buffer);
            return new PooledEnvelopeBuffer(buffer, encodedLength);
        }
        catch
        {
            ExactByteArrayPool.Instance.Return(buffer);
            throw;
        }
    }

    internal static RecordedFrameEnvelope Deserialize(byte[] envelopeBytes) =>
        MessagePackSerializer.Deserialize<RecordedFrameEnvelope>(envelopeBytes.AsMemory(), Options)
        ?? throw new FormatException("The recording envelope was MessagePack null.");
}

internal sealed class PooledEnvelopeBuffer(byte[] buffer, int writtenCount) : IDisposable
{
    private byte[]? _buffer = buffer;

    internal byte[] Bytes => _buffer ?? throw new ObjectDisposedException(nameof(PooledEnvelopeBuffer));

    internal int WrittenCount { get; } = writtenCount;

    public void Dispose()
    {
        byte[]? released = Interlocked.Exchange(ref _buffer, null);
        if (released is not null)
        {
            ExactByteArrayPool.Instance.Return(released);
        }
    }
}

internal sealed class ExactByteArrayPool : ArrayPool<byte>
{
    private const int MaximumRetainedArrays = 8;
    private static readonly ExactByteArrayPool Pool = new();
    private readonly object _gate = new();
    private readonly Dictionary<int, Stack<byte[]>> _available = [];
    private int _retainedCount;
    private int _outstandingCount;

    internal static ExactByteArrayPool Instance => Pool;

    internal int OutstandingCount => Volatile.Read(ref _outstandingCount);

    public override byte[] Rent(int minimumLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumLength);
        lock (_gate)
        {
            if (_available.TryGetValue(minimumLength, out Stack<byte[]>? arrays) && arrays.TryPop(out byte[]? buffer))
            {
                _retainedCount--;
                if (arrays.Count == 0)
                {
                    _available.Remove(minimumLength);
                }

                Interlocked.Increment(ref _outstandingCount);
                return buffer;
            }
        }

        byte[] newBuffer = new byte[minimumLength];
        Interlocked.Increment(ref _outstandingCount);
        return newBuffer;
    }

    public override void Return(byte[] array, bool clearArray = false)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (clearArray)
        {
            Array.Clear(array);
        }

        if (Interlocked.Decrement(ref _outstandingCount) < 0)
        {
            Interlocked.Increment(ref _outstandingCount);
            throw new InvalidOperationException("A recording byte array was returned more than once.");
        }

        lock (_gate)
        {
            if (_retainedCount >= MaximumRetainedArrays)
            {
                return;
            }

            if (!_available.TryGetValue(array.Length, out Stack<byte[]>? arrays))
            {
                arrays = new Stack<byte[]>();
                _available.Add(array.Length, arrays);
            }

            arrays.Push(array);
            _retainedCount++;
        }
    }
}

internal sealed class CountingBufferWriter : IBufferWriter<byte>
{
    private readonly byte[] _scratch;
    private int _writtenCount;

    internal CountingBufferWriter(int capacity)
    {
        _scratch = new byte[capacity];
    }

    internal int WrittenCount => _writtenCount;

    internal ReadOnlySpan<byte> WrittenSpan => _scratch.AsSpan(0, _writtenCount);

    public void Advance(int count)
    {
        if (count < 0 || count > _scratch.Length - _writtenCount)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        _writtenCount += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        _ = GetSpan(sizeHint);
        return _scratch.AsMemory(_writtenCount);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        int requested = Math.Max(sizeHint, 1);
        if (requested > _scratch.Length - _writtenCount)
        {
            throw new InvalidOperationException("The recording envelope exceeds its preallocated serialization scratch buffer.");
        }

        return _scratch.AsSpan(_writtenCount);
    }

    internal void Reset() => _writtenCount = 0;
}
