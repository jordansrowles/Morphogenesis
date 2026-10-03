namespace Rowles.Morphogenesis.Server.Persistence;

public sealed class RecordingStorageLimitExceededException(string message)
    : IOException(message);
