using Rowles.Morphogenesis.Desktop.Networking;

namespace Rowles.Morphogenesis.Desktop.Tests.Testing;

internal sealed class FakeLaboratoryApiClient(SessionDto session) : ILaboratoryApiClient
{
    private Uri _baseAddress = new("http://127.0.0.1:5080/");

    internal SessionDto Session { get; set; } = session;
    internal List<(string Command, SessionCommandRequestDto Request)> Commands { get; } = [];
    internal List<long> RecordingFrameRequests { get; } = [];
    internal int GetSessionCalls { get; private set; }
    internal Func<string, SessionCommandRequestDto, CommandResponse>? CommandHandler { get; set; }

    public Uri BaseAddress => _baseAddress;

    public void SetBaseAddress(Uri baseAddress) => _baseAddress = baseAddress;

    public Task<IReadOnlyList<ExperimentSummaryDto>> GetExperimentsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExperimentSummaryDto>>([]);

    public Task<IReadOnlyList<SessionDto>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SessionDto>>([]);

    public Task<SessionDto> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        GetSessionCalls++;
        return Task.FromResult(Session);
    }

    public Task<SessionDto> CreateSessionAsync(CreateSessionRequestDto request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Session);

    public Task<CommandResponse> SendCommandAsync(
        Guid sessionId,
        string command,
        SessionCommandRequestDto request,
        CancellationToken cancellationToken = default)
    {
        Commands.Add((command, request));
        if (CommandHandler is not null)
            return Task.FromResult(CommandHandler(command, request));

        SessionStatusDto status = command switch
        {
            "start" or "resume" => SessionStatusDto.Running,
            "pause" or "step" => SessionStatusDto.Paused,
            "stop" => SessionStatusDto.Cancelled,
            _ => Session.Status
        };
        Session = Session with { Status = status, Revision = Session.Revision + 1, CurrentMcs = Session.CurrentMcs + (command == "step" ? 1 : 0) };
        return Task.FromResult(new CommandResponse(Session, Conflict: false, NotFound: false));
    }

    public Task<IReadOnlyList<PersistedMetricSampleDto>> GetMetricsAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PersistedMetricSampleDto>>([]);

    public Task<CellInspectionDto> InspectCellAsync(Guid sessionId, int cellId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CellInspectionDto(Session.CurrentMcs, cellId, 1, "Type A", 8, 12, 10, 2, 14, 1));

    public Task<RecordingDto> GetRecordingAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RecordingDto(sessionId, Session.RecordingState, null, 1, 1, 20, "lz4", 0, null, 0, []));

    public Task<RecordingFrameResponse> GetRecordingFrameAsync(Guid sessionId, long mcs, CancellationToken cancellationToken = default)
    {
        RecordingFrameRequests.Add(mcs);
        byte[] payload = FullFrameProtocol.Encode(mcs / 10, mcs, 1, 1, [1]);
        return Task.FromResult(new RecordingFrameResponse(payload, "1", mcs, mcs / 10));
    }

    public Uri GetStreamUri(Guid sessionId) => new(_baseAddress, $"api/sessions/{sessionId:D}/stream");

    public void Dispose()
    {
    }
}
