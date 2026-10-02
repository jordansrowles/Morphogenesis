namespace Rowles.Morphogenesis.Laboratory.Recording;

public sealed record RecordingCompletion(
    DateTimeOffset CompletedAtUtc,
    long FinalMcs,
    long FrameCount);
