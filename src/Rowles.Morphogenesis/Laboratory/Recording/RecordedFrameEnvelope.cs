using MessagePack;

namespace Rowles.Morphogenesis.Laboratory.Recording;

[MessagePackObject]
public sealed partial record RecordedFrameEnvelope(
    [property: Key(0)] int EnvelopeVersion,
    [property: Key(1)] long Sequence,
    [property: Key(2)] long Mcs,
    [property: Key(3)] RecordingFrameKind Kind,
    [property: Key(4)] int PayloadCodecVersion,
    [property: Key(5)] string Compression,
    [property: Key(6)] int Width,
    [property: Key(7)] int Height,
    [property: Key(8)] int UncompressedPayloadSize,
    [property: Key(9)] byte[] Payload);
