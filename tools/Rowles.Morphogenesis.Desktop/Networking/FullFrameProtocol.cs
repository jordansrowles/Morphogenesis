using System.Buffers.Binary;
using System.IO;

namespace Rowles.Morphogenesis.Desktop.Networking;

public readonly record struct FullFrameHeader(
    byte ProtocolVersion,
    byte MessageType,
    long Sequence,
    long Mcs,
    int Width,
    int Height,
    int CellCount);

public sealed class FullFrameBuffer : IDisposable
{
    private readonly FullFrameBufferPool? _pool;
    private int[]? _cellIds;

    public FullFrameBuffer(int siteCount)
    {
        _cellIds = new int[siteCount];
    }

    internal FullFrameBuffer(int[] cellIds, FullFrameBufferPool pool)
    {
        _cellIds = cellIds;
        _pool = pool;
    }

    public int[] CellIds => Volatile.Read(ref _cellIds) ?? throw new ObjectDisposedException(nameof(FullFrameBuffer));
    public FullFrameHeader Header { get; private set; }

    internal void SetHeader(FullFrameHeader header) => Header = header;

    public void Dispose()
    {
        int[]? cellIds = Interlocked.Exchange(ref _cellIds, null);
        if (cellIds is not null)
            _pool?.Return(cellIds);
    }
}

public static class FullFrameProtocol
{
    public const byte CurrentVersion = 1;
    public const byte FullFrameMessageType = 1;
    public const int HeaderLength = 30;

    public static int GetMessageLength(int width, int height)
    {
        int cellCount = GetCellCount(width, height);
        try
        {
            return checked(HeaderLength + checked(cellCount * sizeof(int)));
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Full-frame dimensions exceed the supported message length.", exception);
        }
    }

    public static byte[] Encode(long sequence, long mcs, int width, int height, ReadOnlySpan<int> cellIds)
    {
        int cellCount = GetCellCount(width, height);
        if (cellIds.Length != cellCount)
            throw new ArgumentException("Cell count does not match frame dimensions.", nameof(cellIds));

        byte[] message = new byte[GetMessageLength(width, height)];
        Write(message, sequence, mcs, width, height, cellIds);
        return message;
    }

    public static void Write(Span<byte> destination, long sequence, long mcs, int width, int height, ReadOnlySpan<int> cellIds)
    {
        int cellCount = GetCellCount(width, height);
        int messageLength = GetMessageLength(width, height);
        if (cellIds.Length != cellCount)
            throw new ArgumentException("Cell count does not match frame dimensions.", nameof(cellIds));
        if (destination.Length != messageLength)
            throw new ArgumentException("Destination length does not match the full-frame message length.", nameof(destination));

        destination[0] = CurrentVersion;
        destination[1] = FullFrameMessageType;
        BinaryPrimitives.WriteInt64LittleEndian(destination[2..10], sequence);
        BinaryPrimitives.WriteInt64LittleEndian(destination[10..18], mcs);
        BinaryPrimitives.WriteInt32LittleEndian(destination[18..22], width);
        BinaryPrimitives.WriteInt32LittleEndian(destination[22..26], height);
        BinaryPrimitives.WriteInt32LittleEndian(destination[26..30], cellCount);
        for (int index = 0, offset = HeaderLength; index < cellCount; index++, offset += sizeof(int))
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, sizeof(int)), cellIds[index]);
    }

    public static FullFrameHeader ReadHeader(ReadOnlySpan<byte> message)
    {
        if (message.Length < HeaderLength)
            throw new InvalidDataException("Full-frame message is shorter than its header.");
        if (message[0] != CurrentVersion)
            throw new InvalidDataException($"Unsupported full-frame protocol version {message[0]}.");
        if (message[1] != FullFrameMessageType)
            throw new InvalidDataException($"Unsupported full-frame message type {message[1]}.");

        long sequence = BinaryPrimitives.ReadInt64LittleEndian(message[2..10]);
        long mcs = BinaryPrimitives.ReadInt64LittleEndian(message[10..18]);
        int width = BinaryPrimitives.ReadInt32LittleEndian(message[18..22]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(message[22..26]);
        int cellCount = BinaryPrimitives.ReadInt32LittleEndian(message[26..30]);
        int expectedCellCount = GetCellCount(width, height);
        if (cellCount != expectedCellCount)
            throw new InvalidDataException("Full-frame cell count does not match its dimensions.");
        int expectedLength = GetMessageLength(width, height);
        if (message.Length != expectedLength)
            throw new InvalidDataException("Full-frame message length does not match its dimensions.");
        return new FullFrameHeader(message[0], message[1], sequence, mcs, width, height, cellCount);
    }

    public static FullFrameBuffer Decode(ReadOnlySpan<byte> message)
    {
        FullFrameHeader header = ReadHeader(message);
        FullFrameBuffer frame = new(header.CellCount);
        DecodeInto(message, frame.CellIds, header);
        frame.SetHeader(header);
        return frame;
    }

    public static FullFrameHeader DecodeInto(ReadOnlySpan<byte> message, Span<int> cellIds)
    {
        FullFrameHeader header = ReadHeader(message);
        DecodeInto(message, cellIds, header);
        return header;
    }

    private static void DecodeInto(ReadOnlySpan<byte> message, Span<int> cellIds, FullFrameHeader header)
    {
        if (cellIds.Length != header.CellCount)
            throw new InvalidDataException("Destination cell count does not match the full-frame message.");
        for (int index = 0, offset = HeaderLength; index < header.CellCount; index++, offset += sizeof(int))
            cellIds[index] = BinaryPrimitives.ReadInt32LittleEndian(message.Slice(offset, sizeof(int)));
    }

    private static int GetCellCount(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new InvalidDataException("Full-frame dimensions must be positive.");
        try
        {
            return checked(width * height);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Full-frame dimensions exceed the supported cell count.", exception);
        }
    }
}
