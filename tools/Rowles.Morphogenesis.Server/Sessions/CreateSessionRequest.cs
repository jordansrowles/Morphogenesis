namespace Rowles.Morphogenesis.Server.Sessions;

public sealed record CreateSessionRequest(
    string? ExperimentId,
    int ReplicateIndex = 0,
    bool RecordingEnabled = false,
    int LivePublishMaxFps = 10);
