namespace Rowles.Morphogenesis.Laboratory.Recording;

public static class RecordingFormat
{
    public const int SchemaVersion = 1;

    public const int EnvelopeVersion = 1;

    public const int KeyframeCodecVersion = 1;

    public const int DeltaCodecVersion = 1;

    public const string Compression = "messagepack-lz4-block-array";
}
