namespace Rowles.Morphogenesis.Laboratory.Sessions;

public sealed record SimulationSessionOptions
{
    public int LivePublishMaxFps { get; init; } = 10;

    public int CommandQueueCapacity { get; init; } = 64;

    public int MaxLiveSubscribers { get; init; } = 8;

    public int FrameBufferCount { get; init; } = 10;

    internal void Validate()
    {
        if (LivePublishMaxFps is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(LivePublishMaxFps), "Live frame publication must be between 1 and 20 frames per second.");
        }

        if (CommandQueueCapacity != 64)
        {
            throw new ArgumentException("M4 session command queues have a fixed capacity of 64.", nameof(CommandQueueCapacity));
        }

        if (MaxLiveSubscribers != 8)
        {
            throw new ArgumentException("M4 sessions support exactly eight live subscribers.", nameof(MaxLiveSubscribers));
        }

        if (FrameBufferCount != 10)
        {
            throw new ArgumentException("M4 sessions use exactly ten reusable frame buffers.", nameof(FrameBufferCount));
        }
    }
}
