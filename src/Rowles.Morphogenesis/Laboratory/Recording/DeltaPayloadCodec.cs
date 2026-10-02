namespace Rowles.Morphogenesis.Laboratory.Recording;

public static class DeltaPayloadCodec
{
    public static int GetBufferSize(int siteCount)
    {
        if (siteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(siteCount));
        }

        return checked(10 * siteCount + 64);
    }

    public static int Encode(
        ReadOnlySpan<int> sortedIndices,
        ReadOnlySpan<int> newCellIds,
        int siteCount,
        Span<byte> destination)
    {
        if (siteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(siteCount));
        }

        if (sortedIndices.Length != newCellIds.Length || sortedIndices.Length > siteCount)
        {
            throw new ArgumentException("Delta index and cell-ID spans must have the same valid length.");
        }

        int offset = 0;
        WriteVarUInt32(checked((uint)sortedIndices.Length), destination, ref offset);
        int previousIndex = 0;
        for (int item = 0; item < sortedIndices.Length; item++)
        {
            int index = sortedIndices[item];
            if ((uint)index >= (uint)siteCount || (item > 0 && index <= previousIndex))
            {
                throw new ArgumentOutOfRangeException(nameof(sortedIndices), "Delta indexes must be strictly increasing and within the lattice.");
            }

            int cellId = newCellIds[item];
            if (cellId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(newCellIds), "Recorded cell IDs must be non-negative.");
            }

            WriteVarUInt32(checked((uint)(index - previousIndex)), destination, ref offset);
            WriteVarUInt32(checked((uint)cellId), destination, ref offset);
            previousIndex = index;
        }

        return offset;
    }

    public static int Decode(
        ReadOnlySpan<byte> payload,
        int siteCount,
        Span<int> indicesDestination,
        Span<int> cellIdsDestination)
    {
        if (siteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(siteCount));
        }

        int offset = 0;
        uint rawCount = ReadVarUInt32(payload, ref offset);
        if (rawCount > (uint)siteCount)
        {
            throw new FormatException("The delta change count exceeds the lattice size.");
        }

        int count = checked((int)rawCount);
        if (indicesDestination.Length < count || cellIdsDestination.Length < count)
        {
            throw new ArgumentException("The destination spans are too short for the decoded delta.");
        }

        uint previousIndex = 0;
        for (int item = 0; item < count; item++)
        {
            uint delta = ReadVarUInt32(payload, ref offset);
            uint cellId = ReadVarUInt32(payload, ref offset);
            ulong reconstructed = (ulong)previousIndex + delta;
            if (reconstructed >= (uint)siteCount || (item > 0 && reconstructed <= previousIndex))
            {
                throw new FormatException("The delta contains a repeated or out-of-range lattice index.");
            }

            if (cellId > int.MaxValue)
            {
                throw new FormatException("The delta cell ID exceeds the supported signed Int32 range.");
            }

            indicesDestination[item] = (int)reconstructed;
            cellIdsDestination[item] = (int)cellId;
            previousIndex = (uint)reconstructed;
        }

        if (offset != payload.Length)
        {
            throw new FormatException("The delta payload contains trailing bytes.");
        }

        return count;
    }

    private static void WriteVarUInt32(uint value, Span<byte> destination, ref int offset)
    {
        do
        {
            if ((uint)offset >= (uint)destination.Length)
            {
                throw new ArgumentException("The destination is too short for the encoded delta.", nameof(destination));
            }

            byte next = (byte)(value & 0x7f);
            value >>= 7;
            destination[offset++] = value == 0 ? next : (byte)(next | 0x80);
        }
        while (value != 0);
    }

    private static uint ReadVarUInt32(ReadOnlySpan<byte> payload, ref int offset)
    {
        uint value = 0;
        for (int byteIndex = 0; byteIndex < 5; byteIndex++)
        {
            if ((uint)offset >= (uint)payload.Length)
            {
                throw new FormatException("The delta payload ends inside a varint.");
            }

            byte next = payload[offset++];
            byte data = (byte)(next & 0x7f);
            if (byteIndex == 4 && (data > 0x0f || (next & 0x80) != 0))
            {
                throw new FormatException("The delta payload contains a varint that exceeds 32 bits or five bytes.");
            }

            value |= (uint)data << (byteIndex * 7);
            if ((next & 0x80) == 0)
            {
                return value;
            }
        }

        throw new FormatException("The delta payload contains a varint longer than five bytes.");
    }
}
