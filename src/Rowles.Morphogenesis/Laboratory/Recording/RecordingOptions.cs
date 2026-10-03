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
            throw new ArgumentException("The keyframe cadence is fixed at 20 recorded frames.", nameof(KeyframeEveryRecordedFrames));
        }

        if (DeltaPromotionRatio != 0.75)
        {
            throw new ArgumentException("The delta promotion ratio is fixed at 0.75.", nameof(DeltaPromotionRatio));
        }

        if (WriterQueueCapacity != 8)
        {
            throw new ArgumentException("The recording writer queue capacity is fixed at 8.", nameof(WriterQueueCapacity));
        }
    }
}
