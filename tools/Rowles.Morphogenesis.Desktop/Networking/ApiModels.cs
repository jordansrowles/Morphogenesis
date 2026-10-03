using System.Net;

namespace Rowles.Morphogenesis.Desktop.Networking;

public enum SessionStatusDto
{
    Created = 0,
    Running = 1,
    Paused = 2,
    Completed = 3,
    Failed = 4,
    Interrupted = 5,
    Cancelled = 6
}

public enum RecordingStateDto
{
    Disabled = 0,
    Active = 1,
    Completed = 2,
    Failed = 3
}

public sealed record SessionStaticMetadataDto(string BoundaryMode, int[] CellTypeByCellId);

public sealed record SessionDto(
    Guid SessionId,
    string ExperimentId,
    string ReplicateId,
    int ReplicateIndex,
    SessionStatusDto Status,
    long Revision,
    long CurrentMcs,
    long TargetMcs,
    int Width,
    int Height,
    string KernelId,
    RecordingStateDto RecordingState,
    string? RecordingFailure,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    SessionStaticMetadataDto? Metadata);

public sealed record ExperimentSummaryDto(
    string ExperimentId,
    string Name,
    int GridWidth,
    int GridHeight,
    int McsCount,
    int ReplicateCount,
    string BoundaryMode,
    IReadOnlyList<string> CellTypeNames);

public sealed record CreateSessionRequestDto(
    string ExperimentId,
    int ReplicateIndex,
    bool RecordingEnabled,
    int LivePublishMaxFps);

public sealed record SessionCommandRequestDto(Guid CommandId, long ExpectedRevision);

public sealed record RecordingFrameDto(long Sequence, long Mcs, int Kind);

public sealed record RecordingDto(
    Guid SessionId,
    RecordingStateDto RecordingState,
    string? RecordingFailure,
    int RecordingSchemaVersion,
    int RecordEveryMcs,
    int KeyframeEveryRecordedFrames,
    string Compression,
    long? FirstMcs,
    long? LastMcs,
    int FrameCount,
    IReadOnlyList<RecordingFrameDto> Frames);

public sealed record PersistedMetricSampleDto(long Mcs, IReadOnlyDictionary<string, double> Values);

public sealed record CellInspectionDto(
    long Mcs,
    int CellId,
    int CellTypeId,
    string CellTypeName,
    int Area,
    int Perimeter,
    double TargetArea,
    double AreaStiffness,
    double TargetPerimeter,
    double PerimeterStiffness);

public sealed record CommandResponse(
    SessionDto? Session,
    bool Conflict,
    bool NotFound,
    bool Failed = false,
    string? Error = null);

public sealed record CommandFailureDto(string? Error, string? Failure, SessionDto? Session);

public sealed record RecordingFrameResponse(byte[] Payload, string Protocol, long Mcs, long Sequence);

public sealed class LaboratoryApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public interface ILaboratoryApiClient : IDisposable
{
    Uri BaseAddress { get; }
    void SetBaseAddress(Uri baseAddress);
    Task<IReadOnlyList<ExperimentSummaryDto>> GetExperimentsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SessionDto>> GetSessionsAsync(CancellationToken cancellationToken = default);
    Task<SessionDto> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<SessionDto> CreateSessionAsync(CreateSessionRequestDto request, CancellationToken cancellationToken = default);
    Task<CommandResponse> SendCommandAsync(Guid sessionId, string command, SessionCommandRequestDto request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PersistedMetricSampleDto>> GetMetricsAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<CellInspectionDto> InspectCellAsync(Guid sessionId, int cellId, CancellationToken cancellationToken = default);
    Task<RecordingDto> GetRecordingAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<RecordingFrameResponse> GetRecordingFrameAsync(Guid sessionId, long mcs, CancellationToken cancellationToken = default);
    Uri GetStreamUri(Guid sessionId);
}
