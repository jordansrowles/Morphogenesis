namespace Rowles.Morphogenesis.Laboratory.Recording;

public sealed class RecordingBackpressureException : InvalidOperationException
{
    public RecordingBackpressureException()
        : base("The recording writer queue is full; recording stopped to avoid silently dropping a frame.")
    {
    }
}
