using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Rowles.Morphogenesis.Desktop.Networking;
using Xunit;

namespace Rowles.Morphogenesis.Desktop.Tests.Networking;

public sealed class LaboratoryApiClientTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CreateSessionUsesTheServerRequestContract()
    {
        HttpRequestMessage? captured = null;
        SessionDto expected = CreateSession();
        using LaboratoryApiClient client = CreateClient((request, _) =>
        {
            captured = request;
            return Task.FromResult(Json(HttpStatusCode.Created, expected));
        });

        SessionDto session = await client.CreateSessionAsync(new CreateSessionRequestDto("E02-sorting-v1", 3, true, 20));

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/api/sessions", captured.RequestUri!.AbsolutePath);
        using JsonDocument body = await JsonDocument.ParseAsync(await captured.Content!.ReadAsStreamAsync());
        Assert.Equal("E02-sorting-v1", body.RootElement.GetProperty("experimentId").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("replicateIndex").GetInt32());
        Assert.True(body.RootElement.GetProperty("recordingEnabled").GetBoolean());
        Assert.Equal(20, body.RootElement.GetProperty("livePublishMaxFps").GetInt32());
        Assert.Equal(expected.SessionId, session.SessionId);
    }

    [Fact]
    public async Task ConflictReturnsTheAuthoritativeStateWithoutRetrying()
    {
        int requestCount = 0;
        SessionDto authoritative = CreateSession() with { Status = SessionStatusDto.Paused, Revision = 9 };
        using LaboratoryApiClient client = CreateClient((_, _) =>
        {
            requestCount++;
            return Task.FromResult(Json(HttpStatusCode.Conflict, authoritative));
        });

        CommandResponse result = await client.SendCommandAsync(authoritative.SessionId, "resume",
            new SessionCommandRequestDto(Guid.NewGuid(), 4));

        Assert.True(result.Conflict);
        Assert.False(result.NotFound);
        Assert.Equal(authoritative.SessionId, result.Session!.SessionId);
        Assert.Equal(authoritative.Status, result.Session.Status);
        Assert.Equal(authoritative.Revision, result.Session.Revision);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task FailedCommandReturnsTheAuthoritativeTerminalSession()
    {
        SessionDto failed = CreateSession() with { Status = SessionStatusDto.Failed, Revision = 3 };
        using LaboratoryApiClient client = CreateClient((_, _) => Task.FromResult(Json(HttpStatusCode.InternalServerError, new
        {
            error = "The simulation command failed while applying at its boundary.",
            failure = "InvalidOperationException: Injected failure.",
            session = failed
        })));

        CommandResponse response = await client.SendCommandAsync(
            failed.SessionId,
            "step",
            new SessionCommandRequestDto(Guid.NewGuid(), 2));

        Assert.True(response.Failed);
        Assert.Equal("InvalidOperationException: Injected failure.", response.Error);
        Assert.Equal(failed.SessionId, response.Session!.SessionId);
        Assert.Equal(SessionStatusDto.Failed, response.Session.Status);
        Assert.Equal(3, response.Session.Revision);
    }

    [Fact]
    public async Task NotFoundAndMetricsInspectionAndRecordedFrameResponsesAreDecoded()
    {
        Guid sessionId = Guid.NewGuid();
        byte[] frameBytes = FullFrameProtocol.Encode(5, 20, 1, 1, [3]);
        using LaboratoryApiClient client = CreateClient((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == $"/api/sessions/{sessionId:D}")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (request.RequestUri.AbsolutePath.EndsWith("/metrics", StringComparison.Ordinal))
                return Task.FromResult(Json(HttpStatusCode.OK, new[]
                {
                    new PersistedMetricSampleDto(20, new Dictionary<string, double> { ["cell-area.mean"] = 6.5 })
                }));
            if (request.RequestUri.AbsolutePath.EndsWith("/cells/3", StringComparison.Ordinal))
                return Task.FromResult(Json(HttpStatusCode.OK,
                    new CellInspectionDto(20, 3, 1, "Type A", 6, 10, 8, 2, 12, 1)));
            if (request.RequestUri.AbsolutePath.EndsWith("/recording/frame", StringComparison.Ordinal))
            {
                HttpResponseMessage response = new(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(frameBytes)
                };
                response.Headers.Add("X-Morphogenesis-Frame-Protocol", "1");
                response.Headers.Add("X-Morphogenesis-Frame-Mcs", "20");
                response.Headers.Add("X-Morphogenesis-Frame-Sequence", "5");
                return Task.FromResult(response);
            }
            return Task.FromResult(Json(HttpStatusCode.OK, new object()));
        });

        LaboratoryApiException missing = await Assert.ThrowsAsync<LaboratoryApiException>(() => client.GetSessionAsync(sessionId));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        IReadOnlyList<PersistedMetricSampleDto> metrics = await client.GetMetricsAsync(sessionId);
        Assert.Equal(6.5, metrics[0].Values["cell-area.mean"]);
        CellInspectionDto inspection = await client.InspectCellAsync(sessionId, 3);
        Assert.Equal("Type A", inspection.CellTypeName);
        RecordingFrameResponse recordingFrame = await client.GetRecordingFrameAsync(sessionId, 20);
        Assert.Equal("1", recordingFrame.Protocol);
        Assert.Equal(20, recordingFrame.Mcs);
        Assert.Equal(5, recordingFrame.Sequence);
        Assert.Equal(frameBytes, recordingFrame.Payload);
        Assert.Equal(new Uri("ws://127.0.0.1:5080/api/sessions/" + sessionId.ToString("D") + "/stream"),
            client.GetStreamUri(sessionId));
    }

    private static LaboratoryApiClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) =>
        new(new Uri("http://127.0.0.1:5080"), new StubHandler(callback));

    private static HttpResponseMessage Json<T>(HttpStatusCode statusCode, T value) => new(statusCode)
    {
        Content = JsonContent.Create(value, options: _jsonOptions)
    };

    internal static SessionDto CreateSession() => new(
        Guid.NewGuid(), "E02-sorting-v1", "E02-sorting-v1-r000", 0, SessionStatusDto.Created, 0, 0, 1000,
        2, 2, "canonical", RecordingStateDto.Disabled, null, DateTimeOffset.UtcNow,
        null, null, new SessionStaticMetadataDto("Periodic", [0, 1]));

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            callback(request, cancellationToken);
    }
}
