using System.Buffers.Binary;
using Rowles.Morphogenesis.Desktop.Networking;
using Xunit;

namespace Rowles.Morphogenesis.Desktop.Tests.Networking;

public sealed class FullFrameProtocolTests
{
    [Fact]
    public void EncoderAndDecoderMatchTheSharedGoldenFixture()
    {
        byte[] expected = ReadGoldenFrame();
        int[] cellIds = [0, 1, -1, 0x01020304];

        byte[] encoded = FullFrameProtocol.Encode(0x0102030405060708, 42, 2, 2, cellIds);
        FullFrameBuffer decoded = FullFrameProtocol.Decode(expected);

        Assert.Equal(expected, encoded);
        Assert.Equal(FullFrameProtocol.CurrentVersion, decoded.Header.ProtocolVersion);
        Assert.Equal(FullFrameProtocol.FullFrameMessageType, decoded.Header.MessageType);
        Assert.Equal(0x0102030405060708, decoded.Header.Sequence);
        Assert.Equal(42, decoded.Header.Mcs);
        Assert.Equal(2, decoded.Header.Width);
        Assert.Equal(2, decoded.Header.Height);
        Assert.Equal(4, decoded.Header.CellCount);
        Assert.Equal(cellIds, decoded.CellIds);
    }

    [Fact]
    public void InvalidVersionAndMessageTypeAreRejected()
    {
        byte[] wrongVersion = ReadGoldenFrame();
        wrongVersion[0] = 2;
        Assert.Throws<InvalidDataException>(() => FullFrameProtocol.Decode(wrongVersion));

        byte[] wrongType = ReadGoldenFrame();
        wrongType[1] = 2;
        Assert.Throws<InvalidDataException>(() => FullFrameProtocol.Decode(wrongType));
    }

    [Fact]
    public void WrongMessageLengthAndCellCountAreRejected()
    {
        byte[] shortMessage = ReadGoldenFrame()[..^1];
        Assert.Throws<InvalidDataException>(() => FullFrameProtocol.Decode(shortMessage));

        byte[] wrongCellCount = ReadGoldenFrame();
        BinaryPrimitives.WriteInt32LittleEndian(wrongCellCount.AsSpan(26, sizeof(int)), 3);
        Assert.Throws<InvalidDataException>(() => FullFrameProtocol.Decode(wrongCellCount));
    }

    [Fact]
    public void NegativeAndOverflowDimensionsAreRejected()
    {
        byte[] negative = ReadGoldenFrame();
        BinaryPrimitives.WriteInt32LittleEndian(negative.AsSpan(18, sizeof(int)), -1);
        Assert.Throws<InvalidDataException>(() => FullFrameProtocol.Decode(negative));

        byte[] overflow = ReadGoldenFrame();
        BinaryPrimitives.WriteInt32LittleEndian(overflow.AsSpan(18, sizeof(int)), int.MaxValue);
        BinaryPrimitives.WriteInt32LittleEndian(overflow.AsSpan(22, sizeof(int)), 2);
        Assert.Throws<InvalidDataException>(() => FullFrameProtocol.Decode(overflow));
    }

    [Fact]
    public void DestinationCellCountMustMatchExactly()
    {
        byte[] message = ReadGoldenFrame();
        Assert.Throws<InvalidDataException>(() => FullFrameProtocol.DecodeInto(message, new int[5]));
    }

    private static byte[] ReadGoldenFrame() =>
        Convert.FromHexString(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "full-frame-v1.golden.hex")).Trim());
}
