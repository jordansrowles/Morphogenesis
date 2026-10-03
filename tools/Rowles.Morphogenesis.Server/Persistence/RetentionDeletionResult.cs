namespace Rowles.Morphogenesis.Server.Persistence;

public sealed record RetentionDeletionResult(
    IReadOnlyList<Guid> DeletedRunIds,
    long RemainingRunCount,
    long RemainingRecordingBytes);
