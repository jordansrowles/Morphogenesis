using System.Buffers.Binary;

namespace Rowles.Morphogenesis.Laboratory.Recording;

public static class KeyframePayloadCodec
{
    public static int GetPayloadSize(int siteCount)
    {
        if (siteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(siteCount));
        }

        return checked(siteCount * sizeof(int));
    }

    public static int Encode(ReadOnlySpan<int> cellIds, Span<byte> destination)
    {
        int payloadSize = GetPayloadSize(cellIds.Length);
        if (destination.Length < payloadSize)
        {
            throw new ArgumentException("The destination is too short for the keyframe.", nameof(destination));
        }

        for (int index = 0; index < cellIds.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(index * sizeof(int), sizeof(int)), cellIds[index]);
        }

        return payloadSize;
    }

    public static void Decode(ReadOnlySpan<byte> payload, Span<int> cellIds)
    {
        int expectedSize = GetPayloadSize(cellIds.Length);
        if (payload.Length != expectedSize)
        {
            throw new FormatException($"A keyframe payload must contain exactly {expectedSize} bytes.");
        }

        for (int index = 0; index < cellIds.Length; index++)
        {
            cellIds[index] = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(index * sizeof(int), sizeof(int)));
        }
    }
}
