using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Rowles.Morphogenesis.Laboratory.Publication;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Server.Api;
using Rowles.Morphogenesis.Server.Sessions;
using Rowles.Morphogenesis.Server.Tests.Testing;
using Xunit;

namespace Rowles.Morphogenesis.Server.Tests.Api;

public sealed class FullFrameEndpointTests
{
    private const string SortingExperimentId = "E02-sorting-v1";

    [Fact]
    public void ServerEncoderMatchesTheSharedVersionOneGoldenFixture()
    {
        byte[] actual = new byte[FullFrameMessageWriter.GetMessageLength(2, 2)];
        int[] cellIds = [0, 1, -1, 0x01020304];

        FullFrameMessageWriter.Write(
            actual,
            sequence: 0x0102030405060708,
            mcs: 42,
            width: 2,
            height: 2,
            cellIds);

        Assert.Equal(ReadGoldenFrame(), actual);
    }

    [Fact]
    public async Task LiveWebSocketStartsWithTheCurrentFullFrameAndDisconnectLeavesSessionRunning()
    {
        using TemporaryLaboratoryRoot root = new();
        using LaboratoryFactory factory = new(root.Path);
        using HttpClient http = factory.CreateClient();
        SessionDto created = await CreateSessionAsync(http);
        SessionDto running = await SendCommandAsync(http, created, "start");
        SimulationSessionRegistry registry = factory.Services.GetRequiredService<SimulationSessionRegistry>();
        Assert.True(registry.TryGetSession(created.SessionId, out var session));

        WebSocketClient socketClient = factory.Server.CreateWebSocketClient();
        using WebSocket socket = await socketClient.ConnectAsync(
            new Uri($"ws://localhost/api/sessions/{created.SessionId:D}/stream"),
            CancellationToken.None);
        byte[] message = await ReceiveMessageAsync(socket, checked(FullFrameMessageWriter.HeaderLength + running.Width * running.Height * sizeof(int)));

        Assert.Equal(FullFrameMessageWriter.ProtocolVersion, message[0]);
        Assert.Equal(FullFrameMessageWriter.FullFrameMessageType, message[1]);
        Assert.Equal(running.Width, BinaryPrimitives.ReadInt32LittleEndian(message.AsSpan(18)));
        Assert.Equal(running.Height, BinaryPrimitives.ReadInt32LittleEndian(message.AsSpan(22)));
        Assert.Equal(checked(running.Width * running.Height), BinaryPrimitives.ReadInt32LittleEndian(message.AsSpan(26)));
        Assert.Equal(checked(FullFrameMessageWriter.HeaderLength + running.Width * running.Height * sizeof(int)), message.Length);
        long displayedMcs = BinaryPrimitives.ReadInt64LittleEndian(message.AsSpan(10));
        Assert.True(displayedMcs <= session.GetSnapshot().CurrentMcs);

        socket.Abort();
        await Task.Delay(50);
        Assert.Equal(Rowles.Morphogenesis.Laboratory.Sessions.SimulationSessionStatus.Running, session.GetSnapshot().Status);
    }

    [Fact]
    public async Task UnknownPersistedOnlyAndOverCapacityStreamsAreRejectedBeforeUpgrade()
    {
        using TemporaryLaboratoryRoot root = new();
        using LaboratoryFactory factory = new(root.Path);
        using HttpClient http = factory.CreateClient();

        using HttpResponseMessage unknown = await http.GetAsync($"/api/sessions/{Guid.NewGuid():D}/stream");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        SessionDto created = await CreateSessionAsync(http);
        SessionDto running = await SendCommandAsync(http, created, "start");
        SessionDto stopped = await SendCommandAsync(http, running, "stop");
        SimulationSessionRegistry registry = factory.Services.GetRequiredService<SimulationSessionRegistry>();
        await registry.RemovePersistedSessionAsync(stopped.SessionId);
        using HttpResponseMessage persistedOnly = await http.GetAsync($"/api/sessions/{stopped.SessionId:D}/stream");
        Assert.Equal(HttpStatusCode.Conflict, persistedOnly.StatusCode);

        SessionDto subscriberFixture = await CreateSessionAsync(http);
        Assert.True(registry.TryGetSession(subscriberFixture.SessionId, out var session));
        List<IAsyncEnumerator<SimulationFrameLease>> subscriptions = [];
        try
        {
            for (int index = 0; index < factory.ResourceLimits.MaxLiveSubscribersPerSession; index++)
                subscriptions.Add(session.WatchFramesAsync().GetAsyncEnumerator());

            using HttpResponseMessage overCapacity = await http.GetAsync($"/api/sessions/{subscriberFixture.SessionId:D}/stream");
            Assert.Equal(HttpStatusCode.TooManyRequests, overCapacity.StatusCode);
        }
        finally
        {
            foreach (IAsyncEnumerator<SimulationFrameLease> subscription in subscriptions)
                await subscription.DisposeAsync();
        }
    }

    [Fact]
    public async Task HistoricalFrameEndpointReturnsReconstructedProtocolAndHeaders()
    {
        using TemporaryLaboratoryRoot root = new();
        using LaboratoryFactory factory = new(root.Path);
        using HttpClient http = factory.CreateClient();
        SessionDto session = await CreateSessionAsync(http, recordingEnabled: true);
        await WaitForRecordedFrameAsync(http, session.SessionId);

        using HttpResponseMessage response = await http.GetAsync($"/api/sessions/{session.SessionId:D}/recording/frame?mcs=0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("1", response.Headers.GetValues("X-Morphogenesis-Frame-Protocol").Single());
        Assert.Equal("0", response.Headers.GetValues("X-Morphogenesis-Frame-Mcs").Single());
        Assert.Equal("0", response.Headers.GetValues("X-Morphogenesis-Frame-Sequence").Single());
        byte[] payload = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(1, payload[0]);
        Assert.Equal(1, payload[1]);
        Assert.Equal(session.Width, BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(18)));
        Assert.Equal(session.Height, BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(22)));
        Assert.Equal(checked(session.Width * session.Height), BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(26)));
        Assert.Equal(checked(FullFrameMessageWriter.HeaderLength + session.Width * session.Height * sizeof(int)), payload.Length);
    }

    private static async Task<SessionDto> CreateSessionAsync(HttpClient http, bool recordingEnabled = false)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            "/api/sessions",
            new CreateSessionRequest(SortingExperimentId, 0, recordingEnabled, 10));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SessionDto>())!;
    }

    private static async Task<SessionDto> SendCommandAsync(HttpClient http, SessionDto session, string command)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            $"/api/sessions/{session.SessionId:D}/{command}",
            new SessionCommandRequest(Guid.NewGuid(), session.Revision));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SessionDto>())!;
    }

    private static byte[] ReadGoldenFrame() => Convert.FromHexString(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "full-frame-v1.golden.hex")).Trim());

    private static async Task WaitForRecordedFrameAsync(HttpClient http, Guid sessionId)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (true)
        {
            using HttpResponseMessage response = await http.GetAsync($"/api/sessions/{sessionId:D}/recording", timeout.Token);
            response.EnsureSuccessStatusCode();
            RecordingDto recording = (await response.Content.ReadFromJsonAsync<RecordingDto>(timeout.Token))!;
            if (recording.FrameCount > 0)
                return;
            await Task.Delay(25, timeout.Token);
        }
    }

    private static async Task<byte[]> ReceiveMessageAsync(WebSocket socket, int expectedLength)
    {
        byte[] buffer = new byte[expectedLength];
        int offset = 0;
        while (true)
        {
            ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(offset), CancellationToken.None);
            Assert.Equal(WebSocketMessageType.Binary, result.MessageType);
            offset += result.Count;
            Assert.True(offset <= expectedLength);
            if (result.EndOfMessage)
                break;
        }

        Assert.Equal(expectedLength, offset);
        return buffer;
    }
}
