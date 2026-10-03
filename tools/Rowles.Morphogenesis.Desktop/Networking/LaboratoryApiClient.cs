using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Rowles.Morphogenesis.Desktop.Networking;

public sealed class LaboratoryApiClient : ILaboratoryApiClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public LaboratoryApiClient(Uri baseUri, HttpMessageHandler? handler = null)
    {
        SetBaseAddress(baseUri);
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _httpClient.BaseAddress = BaseAddress;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public Uri BaseAddress { get; private set; } = null!;

    public void SetBaseAddress(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        if (!baseAddress.IsAbsoluteUri || baseAddress.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(baseAddress.UserInfo))
            throw new ArgumentException("The server base URI must be an absolute HTTP or HTTPS URI without credentials.", nameof(baseAddress));
        string value = baseAddress.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseAddress.AbsoluteUri
            : baseAddress.AbsoluteUri + "/";
        BaseAddress = new Uri(value, UriKind.Absolute);
        if (_httpClient is not null)
            _httpClient.BaseAddress = BaseAddress;
    }

    public async Task<IReadOnlyList<ExperimentSummaryDto>> GetExperimentsAsync(CancellationToken cancellationToken = default) =>
        await GetJsonAsync<ExperimentSummaryDto[]>("api/experiments", cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<SessionDto>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        await GetJsonAsync<SessionDto[]>("api/sessions", cancellationToken).ConfigureAwait(false);

    public Task<SessionDto> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        GetJsonAsync<SessionDto>($"api/sessions/{sessionId:D}", cancellationToken);

    public async Task<SessionDto> CreateSessionAsync(CreateSessionRequestDto request, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("api/sessions", request, _jsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new LaboratoryApiException(response.StatusCode, "The experiment was not found on the server.");
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await DeserializeRequiredAsync<SessionDto>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CommandResponse> SendCommandAsync(
        Guid sessionId,
        string command,
        SessionCommandRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            $"api/sessions/{sessionId:D}/{command}", request, _jsonOptions, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return new CommandResponse(null, Conflict: false, NotFound: true);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            SessionDto? authoritative = await response.Content.ReadFromJsonAsync<SessionDto>(_jsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return new CommandResponse(authoritative, Conflict: true, NotFound: false);
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        SessionDto session = await DeserializeRequiredAsync<SessionDto>(response, cancellationToken).ConfigureAwait(false);
        return new CommandResponse(session, Conflict: false, NotFound: false);
    }

    public async Task<IReadOnlyList<PersistedMetricSampleDto>> GetMetricsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        await GetJsonAsync<PersistedMetricSampleDto[]>($"api/sessions/{sessionId:D}/metrics", cancellationToken).ConfigureAwait(false);

    public Task<CellInspectionDto> InspectCellAsync(Guid sessionId, int cellId, CancellationToken cancellationToken = default) =>
        GetJsonAsync<CellInspectionDto>($"api/sessions/{sessionId:D}/cells/{cellId}", cancellationToken);

    public Task<RecordingDto> GetRecordingAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        GetJsonAsync<RecordingDto>($"api/sessions/{sessionId:D}/recording", cancellationToken);

    public async Task<RecordingFrameResponse> GetRecordingFrameAsync(
        Guid sessionId,
        long mcs,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(
            $"api/sessions/{sessionId:D}/recording/frame?mcs={mcs}", HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        if (!response.Headers.TryGetValues("X-Morphogenesis-Frame-Protocol", out IEnumerable<string>? protocolValues) ||
            !response.Headers.TryGetValues("X-Morphogenesis-Frame-Mcs", out IEnumerable<string>? mcsValues) ||
            !response.Headers.TryGetValues("X-Morphogenesis-Frame-Sequence", out IEnumerable<string>? sequenceValues))
            throw new InvalidDataException("The recording frame response is missing protocol headers.");
        if (!long.TryParse(mcsValues.SingleOrDefault(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out long actualMcs) ||
            !long.TryParse(sequenceValues.SingleOrDefault(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out long sequence))
            throw new InvalidDataException("The recording frame response contains an invalid MCS or sequence header.");
        return new RecordingFrameResponse(
            await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false),
            protocolValues.SingleOrDefault() ?? string.Empty,
            actualMcs,
            sequence);
    }

    public Uri GetStreamUri(Guid sessionId)
    {
        Uri httpUri = new(BaseAddress, $"api/sessions/{sessionId:D}/stream");
        UriBuilder builder = new(httpUri)
        {
            Scheme = httpUri.Scheme == "https" ? "wss" : "ws",
            Port = httpUri.IsDefaultPort ? -1 : httpUri.Port
        };
        return builder.Uri;
    }

    private async Task<T> GetJsonAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(relativePath, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await DeserializeRequiredAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> DeserializeRequiredAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        T? result = await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidDataException("The server returned an empty JSON response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        string detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new LaboratoryApiException(response.StatusCode,
            string.IsNullOrWhiteSpace(detail) ? $"The server returned {(int)response.StatusCode} {response.ReasonPhrase}." : detail);
    }

    public void Dispose() => _httpClient.Dispose();
}
