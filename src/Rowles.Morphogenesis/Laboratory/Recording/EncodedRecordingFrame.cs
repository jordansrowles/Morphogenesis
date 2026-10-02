namespace Rowles.Morphogenesis.Laboratory.Recording;

public sealed record EncodedRecordingFrame(
    long Sequence,
    long Mcs,
    RecordingFrameKind Kind,
    int EnvelopeVersion,
    int PayloadCodecVersion,
    string Compression,
    byte[] EnvelopeBytes,
    int UncompressedPayloadSize);
