namespace Rowles.Morphogenesis.Laboratory.Recording;

public sealed record RecordingOptions
{
    public bool Enabled { get; init; }

    public int RecordEveryMcs { get; init; } = 5;

    public int KeyframeEveryRecordedFrames { get; init; } = 20;

    public double DeltaPromotionRatio { get; init; } = 0.75;

    public int WriterQueueCapacity { get; init; } = 8;

    internal void Validate()
    {
        if (RecordEveryMcs < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(RecordEveryMcs), "Recording cadence must be at least one MCS.");
        }

        if (KeyframeEveryRecordedFrames != 20)
        {
            throw new ArgumentException("M4 recordings use a fixed keyframe cadence of 20 recorded frames.", nameof(KeyframeEveryRecordedFrames));
        }

        if (DeltaPromotionRatio != 0.75)
        {
            throw new ArgumentException("M4 recordings use a fixed delta promotion ratio of 0.75.", nameof(DeltaPromotionRatio));
        }

        if (WriterQueueCapacity != 8)
        {
            throw new ArgumentException("M4 recordings use a fixed writer queue capacity of 8.", nameof(WriterQueueCapacity));
        }
    }
}
