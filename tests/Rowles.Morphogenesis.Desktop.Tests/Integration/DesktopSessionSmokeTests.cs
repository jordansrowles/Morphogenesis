using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Rowles.Morphogenesis.Desktop.Networking;
using Rowles.Morphogenesis.Experiments;
using Xunit;

namespace Rowles.Morphogenesis.Desktop.Tests.Integration;

public sealed class DesktopSessionSmokeTests
{
    [Fact]
    public async Task DesktopClientCreatesControlsDisconnectsReconnectsAndPlaysARecordedSession()
    {
        string root = Path.Combine(Path.GetTempPath(), "morphogenesis-desktop-smoke", Guid.NewGuid().ToString("N"));
        string dataDirectory = Path.Combine(root, "data");
        string logDirectory = Path.Combine(root, "logs");
        string canonicalDirectory = Path.Combine(root, "canonical");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(logDirectory);
        Directory.CreateDirectory(canonicalDirectory);
        CopyCanonicalExperiments(canonicalDirectory);
        ExtendSortingRun(canonicalDirectory);

        ConcurrentQueue<string> serverOutput = new();
        Process server = StartServer(dataDirectory, logDirectory, canonicalDirectory, serverOutput, out Uri serverUri);
        using CancellationTokenSource firstConnection = new();
        using CancellationTokenSource secondConnection = new();
        Task? firstStream = null;
        Task? secondStream = null;

        try
        {
            await WaitForServerAsync(server, serverUri, serverOutput);
            using LaboratoryApiClient api = new(serverUri);
            LiveFrameStreamClient streamClient = new();

            IReadOnlyList<ExperimentSummaryDto> experiments = await api.GetExperimentsAsync();
            Assert.Contains(experiments, experiment => experiment.ExperimentId == "E02-sorting-v1");

            SessionDto session = await api.CreateSessionAsync(new CreateSessionRequestDto(
                "E02-sorting-v1", ReplicateIndex: 0, RecordingEnabled: true, LivePublishMaxFps: 20));
            session = await SendCommandAsync(api, session, "start");
            Assert.Equal(SessionStatusDto.Running, session.Status);

            TaskCompletionSource<(FullFrameHeader Header, int[] CellIds)> firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
            firstStream = streamClient.StreamAsync(api.GetStreamUri(session.SessionId), session.Width, session.Height,
                (frame, _) =>
                {
                    firstFrame.TrySetResult((frame.Header, (int[])frame.CellIds.Clone()));
                    return Task.CompletedTask;
                }, firstConnection.Token);
            (FullFrameHeader firstHeader, int[] cellIds) = await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(session.Width, firstHeader.Width);
            Assert.Equal(session.Height, firstHeader.Height);
            Assert.Equal(session.Width * session.Height, firstHeader.CellCount);
            int cellId = Assert.Single(cellIds.Where(value => value > 0).Take(1));

            session = await SendCommandAsync(api, session, "pause");
            Assert.Equal(SessionStatusDto.Paused, session.Status);
            session = await SendCommandAsync(api, session, "step");
            Assert.Equal(SessionStatusDto.Paused, session.Status);
            CellInspectionDto inspection = await api.InspectCellAsync(session.SessionId, cellId);
            Assert.Equal(cellId, inspection.CellId);
            session = await SendCommandAsync(api, session, "resume");
            Assert.Equal(SessionStatusDto.Running, session.Status);

            firstConnection.Cancel();
            await AssertStreamStopsAsync(firstStream);
            Assert.Equal(SessionStatusDto.Running, (await api.GetSessionAsync(session.SessionId)).Status);

            TaskCompletionSource<FullFrameHeader> reconnectedFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
            secondStream = streamClient.StreamAsync(api.GetStreamUri(session.SessionId), session.Width, session.Height,
                (frame, _) =>
                {
                    reconnectedFrame.TrySetResult(frame.Header);
                    return Task.CompletedTask;
                }, secondConnection.Token);
            FullFrameHeader secondHeader = await reconnectedFrame.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(secondHeader.Mcs >= firstHeader.Mcs);
            secondConnection.Cancel();
            await AssertStreamStopsAsync(secondStream);

            session = await SendCommandAsync(api, session, "stop");
            Assert.Equal(SessionStatusDto.Cancelled, session.Status);

            RecordingDto recording = await WaitForRecordingAsync(api, session.SessionId);
            Assert.True(recording.FrameCount > 0);
            using RecordedFrameClient recordedFrameClient = new(api, session.Width, session.Height);
            using FullFrameBuffer recorded = await recordedFrameClient.GetFrameAsync(session.SessionId, recording.Frames[0].Mcs);
            Assert.Equal(session.Width, recorded.Header.Width);
            Assert.Equal(session.Height, recorded.Header.Height);
        }
        finally
        {
            firstConnection.Cancel();
            secondConnection.Cancel();
            await IgnoreExpectedStreamTerminationAsync(firstStream);
            await IgnoreExpectedStreamTerminationAsync(secondStream);
            if (!server.HasExited)
                server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
            server.Dispose();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<SessionDto> SendCommandAsync(LaboratoryApiClient api, SessionDto session, string command)
    {
        CommandResponse result = await api.SendCommandAsync(session.SessionId, command,
            new SessionCommandRequestDto(Guid.NewGuid(), session.Revision));
        Assert.False(result.Conflict);
        Assert.False(result.NotFound);
        return Assert.IsType<SessionDto>(result.Session);
    }

    private static async Task AssertStreamStopsAsync(Task stream)
    {
        Exception exception = await Assert.ThrowsAnyAsync<Exception>(() => stream);
        Assert.True(exception is OperationCanceledException or IOException,
            $"Expected stream cancellation or transport closure, got {exception.GetType().Name}.");
    }

    private static async Task IgnoreExpectedStreamTerminationAsync(Task? stream)
    {
        if (stream is null)
            return;
        try
        {
            await stream;
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }

    private static async Task<RecordingDto> WaitForRecordingAsync(LaboratoryApiClient api, Guid sessionId)
    {
        RecordingDto? recording = null;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            recording = await api.GetRecordingAsync(sessionId);
            if (recording.FrameCount > 0)
                return recording;
            await Task.Delay(50);
        }
        return recording!;
    }

    private static void CopyCanonicalExperiments(string canonicalDirectory)
    {
        string sourceDirectory = Path.Combine(AppContext.BaseDirectory, "experiments", "canonical");
        foreach (string source in Directory.EnumerateFiles(sourceDirectory, "*.json"))
        {
            string target = Path.Combine(canonicalDirectory, Path.GetFileName(source));
            File.Copy(source, target);
        }
    }

    private static void ExtendSortingRun(string canonicalDirectory)
    {
        string sortingPath = Path.Combine(canonicalDirectory, "E02-sorting.json");
        ExperimentManifest sorting = ExperimentManifest.ReadJson(sortingPath);
        File.WriteAllText(sortingPath, (sorting with { McsCount = 100_000 }).ToJson());
    }

    private static Process StartServer(
        string dataDirectory,
        string logDirectory,
        string canonicalDirectory,
        ConcurrentQueue<string> output,
        out Uri serverUri)
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        serverUri = new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute);

        string serverAssembly = Path.Combine(AppContext.BaseDirectory, "Rowles.Morphogenesis.Server.dll");
        if (!File.Exists(serverAssembly))
            throw new FileNotFoundException("The server application was not copied beside the desktop tests.", serverAssembly);

        ProcessStartInfo startInfo = new("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(serverAssembly);
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";
        startInfo.Environment["Laboratory__BindUrl"] = serverUri.AbsoluteUri.TrimEnd('/');
        startInfo.Environment["Laboratory__DataDirectory"] = dataDirectory;
        startInfo.Environment["Laboratory__LogDirectory"] = logDirectory;
        startInfo.Environment["Laboratory__CanonicalExperimentsDirectory"] = canonicalDirectory;
        startInfo.Environment["Laboratory__DatabaseFileName"] = "laboratory.db";

        Process server = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        server.OutputDataReceived += (_, eventArgs) => EnqueueOutput(eventArgs.Data, output);
        server.ErrorDataReceived += (_, eventArgs) => EnqueueOutput(eventArgs.Data, output);
        if (!server.Start())
            throw new InvalidOperationException("The local laboratory server did not start.");
        server.BeginOutputReadLine();
        server.BeginErrorReadLine();
        return server;
    }

    private static async Task WaitForServerAsync(Process server, Uri serverUri, ConcurrentQueue<string> output)
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(2) };
        Stopwatch timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (server.HasExited)
                throw new InvalidOperationException($"The local server exited with code {server.ExitCode}: {string.Join(Environment.NewLine, output)}");
            try
            {
                using HttpResponseMessage response = await client.GetAsync(new Uri(serverUri, "api/experiments"));
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"The local server did not become ready: {string.Join(Environment.NewLine, output)}");
    }

    private static void EnqueueOutput(string? line, ConcurrentQueue<string> output)
    {
        if (!string.IsNullOrWhiteSpace(line))
            output.Enqueue(line);
    }
}
