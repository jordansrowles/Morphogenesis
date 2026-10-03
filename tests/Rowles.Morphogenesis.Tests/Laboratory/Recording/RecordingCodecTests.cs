using Rowles.Morphogenesis.Laboratory.Recording;

namespace Rowles.Morphogenesis.Tests.Laboratory.Recording;

[Collection(RecordingTestCollection.Name)]
public sealed class RecordingCodecTests
{
    [Fact]
    public void FormatVersionsAndCompressionAreFixed()
    {
        Assert.Equal(1, RecordingFormat.SchemaVersion);
        Assert.Equal(1, RecordingFormat.EnvelopeVersion);
        Assert.Equal(1, RecordingFormat.KeyframeCodecVersion);
        Assert.Equal(1, RecordingFormat.DeltaCodecVersion);
        Assert.Equal("messagepack-lz4-block-array", RecordingFormat.Compression);
    }

    [Fact]
    public void KeyframeUsesSignedLittleEndianInt32AndRequiresExactLength()
    {
        int[] expected = [0, 1, -2, int.MaxValue, int.MinValue];
        byte[] payload = new byte[KeyframePayloadCodec.GetPayloadSize(expected.Length)];
        int encodedSize = KeyframePayloadCodec.Encode(expected, payload);
        int[] actual = new int[expected.Length];

        KeyframePayloadCodec.Decode(payload, actual);

        Assert.Equal(expected.Length * sizeof(int), encodedSize);
        Assert.Equal(expected, actual);
        Assert.Equal([0, 0, 0, 0, 1, 0, 0, 0, 0xfe, 0xff, 0xff, 0xff], payload.AsSpan(0, 12).ToArray());
        Assert.Throws<FormatException>(() => KeyframePayloadCodec.Decode(payload.AsSpan(1), actual));
        Assert.Throws<ArgumentException>(() => KeyframePayloadCodec.Encode(expected, payload.AsSpan(1)));
    }

    [Fact]
    public void EmptyAndSingleSiteDeltasUseUnsignedLeb128()
    {
        byte[] scratch = new byte[DeltaPayloadCodec.GetBufferSize(siteCount: 32)];
        int emptyLength = DeltaPayloadCodec.Encode([], [], 32, scratch);
        Assert.Equal([0], scratch.AsSpan(0, emptyLength).ToArray());
        Assert.Equal(0, DeltaPayloadCodec.Decode(scratch.AsSpan(0, emptyLength), 32, new int[32], new int[32]));

        int singleLength = DeltaPayloadCodec.Encode([5], [7], 32, scratch);
        Assert.Equal([1, 5, 7], scratch.AsSpan(0, singleLength).ToArray());
        int[] indices = new int[32];
        int[] cellIds = new int[32];
        Assert.Equal(1, DeltaPayloadCodec.Decode(scratch.AsSpan(0, singleLength), 32, indices, cellIds));
        Assert.Equal(5, indices[0]);
        Assert.Equal(7, cellIds[0]);
    }

    [Fact]
    public void DeltaEncodesMaximumSigned32BitIndexAndCellId()
    {
        byte[] scratch = new byte[32];

        int length = DeltaPayloadCodec.Encode([int.MaxValue - 1], [int.MaxValue], int.MaxValue, scratch);
        int[] indices = new int[1];
        int[] cellIds = new int[1];
        int count = DeltaPayloadCodec.Decode(scratch.AsSpan(0, length), int.MaxValue, indices, cellIds);

        Assert.Equal(11, length);
        Assert.Equal(1, count);
        Assert.Equal(int.MaxValue - 1, indices[0]);
        Assert.Equal(int.MaxValue, cellIds[0]);
    }

    [Theory]
    [MemberData(nameof(MalformedDeltas))]
    public void DeltaRejectsMalformedVarintsIndexesAndTrailingBytes(byte[] payload, int siteCount)
    {
        Assert.Throws<FormatException>(() => DeltaPayloadCodec.Decode(payload, siteCount, new int[siteCount], new int[siteCount]));
    }

    public static TheoryData<byte[], int> MalformedDeltas => new()
    {
        { [0x80], 8 },
        { [0x80, 0x80, 0x80, 0x80, 0x10], 8 },
        { [0x80, 0x80, 0x80, 0x80, 0x80, 0x00], 8 },
        { [2, 0, 1, 0, 2], 8 },
        { [1, 8, 1], 8 },
        { [0, 1], 8 },
        { [9], 8 }
    };

    [Fact]
    public void DeltaRejectsNegativeIdsAndInvalidIndexOrderAtEncoding()
    {
        byte[] scratch = new byte[128];

        Assert.Throws<ArgumentOutOfRangeException>(() => DeltaPayloadCodec.Encode([0], [-1], 8, scratch));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeltaPayloadCodec.Encode([4, 3], [1, 2], 8, scratch));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeltaPayloadCodec.Encode([8], [1], 8, scratch));
    }

    [Fact]
    public void RandomKeyframesAndCoalescedDeltaChainsReconstructExactly()
    {
        System.Random random = new(0x5eed);
        const int SiteCount = 1024;
        for (int trial = 0; trial < 40; trial++)
        {
            int[] state = new int[SiteCount];
            for (int index = 0; index < state.Length; index++)
            {
                state[index] = random.Next(0, 12);
            }

            byte[] keyframePayload = new byte[KeyframePayloadCodec.GetPayloadSize(SiteCount)];
            KeyframePayloadCodec.Encode(state, keyframePayload);
            int[] reconstructed = new int[SiteCount];
            KeyframePayloadCodec.Decode(keyframePayload, reconstructed);
            Assert.Equal(state, reconstructed);

            CoalescingLatticeChangeAccumulator accumulator = new(SiteCount);
            int changeCount = random.Next(0, SiteCount + 1);
            for (int change = 0; change < changeCount; change++)
            {
                int index = random.Next(SiteCount);
                int newCellId = random.Next(0, 12);
                accumulator.AcceptedCopy(index, oldCellId: state[index], newCellId);
                state[index] = newCellId;
                if (change % 3 == 0)
                {
                    newCellId = random.Next(0, 12);
                    accumulator.AcceptedCopy(index, oldCellId: state[index], newCellId);
                    state[index] = newCellId;
                }
            }

            accumulator.SortChanges(state);
            byte[] deltaPayload = new byte[DeltaPayloadCodec.GetBufferSize(SiteCount)];
            int deltaLength = DeltaPayloadCodec.Encode(
                accumulator.SortedIndices,
                accumulator.SortedCellIds,
                SiteCount,
                deltaPayload);
            int[] indices = new int[SiteCount];
            int[] cellIds = new int[SiteCount];
            int decodedCount = DeltaPayloadCodec.Decode(deltaPayload.AsSpan(0, deltaLength), SiteCount, indices, cellIds);
            for (int change = 0; change < decodedCount; change++)
            {
                reconstructed[indices[change]] = cellIds[change];
            }

            Assert.Equal(state, reconstructed);
        }
    }

    [Fact]
    public void MessagePackLz4EnvelopeHasStableGoldenEncoding()
    {
        RecordedFrameEnvelope envelope = new(
            1,
            0,
            0,
            RecordingFrameKind.Keyframe,
            1,
            RecordingFormat.Compression,
            1,
            1,
            4,
            [1, 2, 3, 4]);
        using PooledEnvelopeBuffer bytes = new RecordingMessagePackCodec(siteCount: 1).Serialize(envelope);

        Assert.Equal("9A0100000101BB6D6573736167657061636B2D6C7A342D626C6F636B2D6172726179010104C40401020304", Convert.ToHexString(bytes.Bytes));
        RecordedFrameEnvelope decoded = RecordingMessagePackCodec.Deserialize(bytes.Bytes);
        Assert.Equal(envelope with { Payload = [] }, decoded with { Payload = [] });
        Assert.Equal(envelope.Payload, decoded.Payload);
    }

    [Fact]
    public void LargeMessagePackEnvelopeCompressesAndRoundTrips()
    {
        byte[] payload = new byte[4096];
        Array.Fill(payload, (byte)7);
        RecordedFrameEnvelope envelope = new(
            1,
            2,
            10,
            RecordingFrameKind.Keyframe,
            1,
            RecordingFormat.Compression,
            32,
            32,
            payload.Length,
            payload);
        using PooledEnvelopeBuffer encoded = new RecordingMessagePackCodec(siteCount: 1024).Serialize(envelope);

        Assert.True(encoded.WrittenCount < payload.Length);
        Assert.Equal(payload, RecordingMessagePackCodec.Deserialize(encoded.Bytes).Payload);
    }

    [Fact]
    public void CompressedSizeExamplesAndPromotionDecisionsAreStable()
    {
        const int Width = 256;
        int siteCount = Width * Width;
        int[] lattice = new int[siteCount];
        Array.Fill(lattice, 1);
        byte[] keyframePayload = new byte[KeyframePayloadCodec.GetPayloadSize(siteCount)];
        KeyframePayloadCodec.Encode(lattice, keyframePayload);
        RecordedFrameEnvelope keyframe = new(
            1, 0, 0, RecordingFrameKind.Keyframe, 1, RecordingFormat.Compression,
            Width, Width, keyframePayload.Length, keyframePayload);
        using PooledEnvelopeBuffer keyframeBytes = new RecordingMessagePackCodec(siteCount).Serialize(keyframe);
        Assert.Equal(262_144, keyframePayload.Length);
        Assert.Equal(1_111, keyframeBytes.WrittenCount);

        int[] changePercentages = [1, 10, 50, 75, 100];
        int[] expectedPayloadSizes = [2_622, 26_214, 131_075, 196_611, 262_147];
        int[] expectedEnvelopeSizes = [96, 189, 604, 872, 1_119];
        bool[] expectedPromotions = [false, false, false, true, true];

        for (int sample = 0; sample < changePercentages.Length; sample++)
        {
            int percent = changePercentages[sample];
            int count = siteCount * percent / 100;
            int[] indices = new int[count];
            int[] cellIds = new int[count];
            for (int item = 0; item < count; item++)
            {
                indices[item] = (int)((long)item * siteCount / count);
                cellIds[item] = 16_384;
            }

            byte[] deltaScratch = new byte[DeltaPayloadCodec.GetBufferSize(siteCount)];
            int deltaLength = DeltaPayloadCodec.Encode(indices, cellIds, siteCount, deltaScratch);
            byte[] deltaPayload = deltaScratch.AsSpan(0, deltaLength).ToArray();
            RecordedFrameEnvelope delta = new(
                1, percent, percent * 5, RecordingFrameKind.Delta, 1, RecordingFormat.Compression,
                Width, Width, deltaLength, deltaPayload);
            using PooledEnvelopeBuffer encoded = new RecordingMessagePackCodec(siteCount).Serialize(delta);

            Assert.Equal(expectedPayloadSizes[sample], deltaLength);
            Assert.Equal(expectedEnvelopeSizes[sample], encoded.WrittenCount);
            Assert.Equal(expectedPromotions[sample], SimulationRecorder.ShouldPromoteDelta(deltaLength, keyframePayload.Length));
        }
    }
}
