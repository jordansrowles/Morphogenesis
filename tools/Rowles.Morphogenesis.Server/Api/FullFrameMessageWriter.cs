using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Rowles.Morphogenesis.Laboratory.Publication;

namespace Rowles.Morphogenesis.Server.Api;

public static class FullFrameMessageWriter
{
    public const byte ProtocolVersion = 1;
    public const byte FullFrameMessageType = 1;
    public const int HeaderLength = 30;

    public static int GetMessageLength(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Frame dimensions must be positive.");

        int cellCount = checked(width * height);
        return checked(HeaderLength + checked(cellCount * sizeof(int)));
    }

    public static void Write(Span<byte> destination, SimulationFrameLease frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Write(
            destination,
            frame.Sequence,
            frame.Mcs,
            frame.Width,
            frame.Height,
            frame.CellIds.Span);
    }

    public static void Write(
        Span<byte> destination,
        long sequence,
        long mcs,
        int width,
        int height,
        ReadOnlySpan<int> cellIds)
    {
        int messageLength = GetMessageLength(width, height);
        int cellCount = checked(width * height);
        if (cellIds.Length != cellCount)
            throw new ArgumentException("Cell count does not match the frame dimensions.", nameof(cellIds));
        if (destination.Length != messageLength)
            throw new ArgumentException("Destination length does not match the full-frame protocol message.", nameof(destination));

        destination[0] = ProtocolVersion;
        destination[1] = FullFrameMessageType;
        BinaryPrimitives.WriteInt64LittleEndian(destination[2..], sequence);
        BinaryPrimitives.WriteInt64LittleEndian(destination[10..], mcs);
        BinaryPrimitives.WriteInt32LittleEndian(destination[18..], width);
        BinaryPrimitives.WriteInt32LittleEndian(destination[22..], height);
        BinaryPrimitives.WriteInt32LittleEndian(destination[26..], cellCount);

        Span<byte> cells = destination[HeaderLength..];
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.AsBytes(cellIds).CopyTo(cells);
            return;
        }

        for (int index = 0; index < cellCount; index++)
            BinaryPrimitives.WriteInt32LittleEndian(cells[(index * sizeof(int))..], cellIds[index]);
    }
}
