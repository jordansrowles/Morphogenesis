using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Playwright;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Xunit;

namespace Rowles.Morphogenesis.Server.Tests.Browser;

public sealed class LaboratoryBrowserTests
{
    [Fact]
    public async Task BrowserCanRunSelectInspectAndPlayBackAnE02SessionAndSendAStaleCommand()
    {
        string root = Path.Combine(Path.GetTempPath(), "morphogenesis-browser-tests", Guid.NewGuid().ToString("N"));
        string dataDirectory = Path.Combine(root, "data");
        string logDirectory = Path.Combine(root, "logs");
        string canonicalDirectory = Path.Combine(root, "canonical");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(logDirectory);
        Directory.CreateDirectory(canonicalDirectory);
        CopyCanonicalExperiments(canonicalDirectory);
        ExtendSortingRun(canonicalDirectory);

        (Process server, Uri serverUri) = StartServer(dataDirectory, logDirectory, canonicalDirectory);
        try
        {
            await WaitForServerAsync(server, serverUri);
            using IPlaywright playwright = await Playwright.CreateAsync();
            await using IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            await using IBrowserContext firstContext = await browser.NewContextAsync();
            IPage firstPage = await firstContext.NewPageAsync();
            ConcurrentQueue<string> browserErrors = new();
            firstPage.PageError += (_, message) => browserErrors.Enqueue(message);
            firstPage.Console += (_, message) =>
            {
                if (message.Type == "error")
                    browserErrors.Enqueue(message.Text);
            };
            await firstPage.GotoAsync(serverUri.ToString());
            await firstPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await firstPage.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Canonical experiments" }).WaitForAsync();
            await CreateSortingSessionAsync(firstPage, record: true);
            Guid recordedSessionId = Guid.Parse(new Uri(firstPage.Url).Segments[^1].TrimEnd('/'));
            await firstPage.GetByTestId("start-session").ClickAsync();
            await WaitForTextAsync(firstPage.GetByTestId("session-status"), "Running");
            await firstPage.GetByTestId("pause-session").ClickAsync();
            await WaitForTextAsync(firstPage.GetByTestId("session-status"), "Paused");
            await firstPage.GetByTestId("step-session").ClickAsync();
            await firstPage.GetByTestId("resume-session").ClickAsync();
            await WaitForTextAsync(firstPage.GetByTestId("session-status"), "Running");

            ILocator canvas = firstPage.GetByTestId("lattice-canvas");
            try
            {
                await WaitForAttributeAsync(canvas, "data-display-mcs");
            }
            catch (Exception exception)
            {
                string canvasState = await firstPage.EvaluateAsync<string>(
                    "() => JSON.stringify({ canvas: document.querySelector('[data-testid=lattice-canvas]')?.outerHTML, label: document.querySelector('.frame-label')?.textContent, counters: document.querySelector('.counter-row')?.textContent, frameError: document.querySelector('[data-testid=frame-error]')?.textContent })");
                throw new InvalidOperationException($"Canvas state: {canvasState}; browser errors: {string.Join(" | ", browserErrors)}", exception);
            }
            JsonElement bounds = await canvas.EvaluateAsync<JsonElement>(
                "element => { const bounds = element.getBoundingClientRect(); return { x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height }; }");
            await firstPage.Mouse.ClickAsync(
                (float)(bounds.GetProperty("x").GetDouble() + bounds.GetProperty("width").GetDouble() / 2),
                (float)(bounds.GetProperty("y").GetDouble() + bounds.GetProperty("height").GetDouble() / 2));
            await firstPage.GetByTestId("cell-inspection").WaitForAsync();
            await firstPage.GetByTestId("view-type").ClickAsync();
            await firstPage.GetByTestId("view-boundary").ClickAsync();
            await firstPage.GetByTestId("view-identity").ClickAsync();
            await firstPage.GetByTestId("stop-session").ClickAsync();
            await WaitForTextAsync(firstPage.GetByTestId("session-status"), "Cancelled");
            await firstPage.GetByTestId("play-recording").WaitForAsync(new LocatorWaitForOptions { Timeout = 20_000 });
            await firstPage.GetByTestId("play-recording").ClickAsync();
            await firstPage.GetByTestId("pause-recording").ClickAsync();
            ILocator seek = firstPage.GetByTestId("recording-seek");
            await seek.EvaluateAsync("(element) => { element.value = element.max; element.dispatchEvent(new Event('change', { bubbles: true })); }");

            await firstPage.GotoAsync(serverUri.ToString());
            await CreateSortingSessionAsync(firstPage, record: false);
            Uri sessionUri = new(firstPage.Url);
            string sessionId = sessionUri.Segments[^1].TrimEnd('/');
            await WaitForTextAsync(firstPage.GetByTestId("session-status"), "Created");

            await using IBrowserContext secondContext = await browser.NewContextAsync();
            IPage secondPage = await secondContext.NewPageAsync();
            await secondPage.GotoAsync(sessionUri.ToString());
            await WaitForTextAsync(secondPage.GetByTestId("session-status"), "Created");
            await firstPage.GetByTestId("start-session").ClickAsync();
            await WaitForTextAsync(firstPage.GetByTestId("session-status"), "Running");
            JsonElement staleCommand = await secondPage.EvaluateAsync<JsonElement>(
                "async (id) => { const response = await fetch(`/api/sessions/${id}/pause`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ commandId: crypto.randomUUID(), expectedRevision: 0 }) }); return { status: response.status, body: await response.json() }; }",
                sessionId);
            Assert.Equal(409, staleCommand.GetProperty("status").GetInt32());
            Assert.Equal((int)SimulationSessionStatus.Running, staleCommand.GetProperty("body").GetProperty("status").GetInt32());
            Assert.Empty(browserErrors);

            Process previousServer = server;
            previousServer.Kill(entireProcessTree: true);
            await previousServer.WaitForExitAsync();
            previousServer.Dispose();
            (server, serverUri) = StartServer(dataDirectory, logDirectory, canonicalDirectory);
            await WaitForServerAsync(server, serverUri);
            await firstPage.GotoAsync(new Uri(serverUri, $"/sessions/{recordedSessionId:D}").ToString());
            await firstPage.GetByTestId("play-recording").WaitForAsync(new LocatorWaitForOptions { Timeout = 20_000 });
            await WaitForAttributeAsync(firstPage.GetByTestId("lattice-canvas"), "data-display-mcs");
        }
        finally
        {
            if (!server.HasExited)
                server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
            server.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CreateSortingSessionAsync(IPage page, bool record)
    {
        ILocator experimentCard = page.Locator(".experiment-card").Filter(new LocatorFilterOptions { HasText = "E02-sorting-v1" });
        await experimentCard.GetByTestId("open-create-session").ClickAsync();
        try
        {
            await page.GetByTestId("create-session").WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            await experimentCard.GetByTestId("open-create-session").ClickAsync();
            try
            {
                await page.GetByTestId("create-session").WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
            }
            catch (TimeoutException)
            {
            string content = await page.ContentAsync();
            throw new InvalidOperationException($"The experiment form did not open. Rendered page: {content}");
            }
        }
        if (record)
            await experimentCard.Locator("input[type=checkbox]").CheckAsync();
        await page.GetByTestId("create-session").ClickAsync();
        await page.WaitForURLAsync("**/sessions/*");
        await page.GetByTestId("session-status").WaitForAsync();
    }

    private static void CopyCanonicalExperiments(string destination)
    {
        string source = Path.Combine(AppContext.BaseDirectory, "experiments", "canonical");
        foreach (string path in Directory.EnumerateFiles(source, "*.json"))
            File.Copy(path, Path.Combine(destination, Path.GetFileName(path)));
    }

    private static void ExtendSortingRun(string directory)
    {
        string path = Path.Combine(directory, "E02-sorting.json");
        ExperimentManifest manifest = ExperimentManifest.ReadJson(path);
        File.WriteAllText(path, (manifest with { McsCount = 100_000, GridWidth = 256, GridHeight = 256 }).ToJson());
    }

    private static (Process Server, Uri Uri) StartServer(string dataDirectory, string logDirectory, string canonicalDirectory)
    {
        string repositoryRoot = FindRepositoryRoot();
        int port;
        using (TcpListener listener = new(System.Net.IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        ProcessStartInfo start = new("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--no-build");
        start.ArgumentList.Add("--no-restore");
        start.ArgumentList.Add("--no-launch-profile");
        start.ArgumentList.Add("--project");
        start.ArgumentList.Add(Path.Combine(repositoryRoot, "tools", "Rowles.Morphogenesis.Server", "Rowles.Morphogenesis.Server.csproj"));
        string configuration = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.Name ?? "Debug";
        start.ArgumentList.Add("--configuration");
        start.ArgumentList.Add(configuration);
        start.Environment["Laboratory__BindUrl"] = $"http://127.0.0.1:{port}";
        start.Environment["Laboratory__DataDirectory"] = dataDirectory;
        start.Environment["Laboratory__LogDirectory"] = logDirectory;
        start.Environment["Laboratory__CanonicalExperimentsDirectory"] = canonicalDirectory;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        Process server = Process.Start(start) ?? throw new InvalidOperationException("The laboratory server process did not start.");
        return (server, new Uri($"http://127.0.0.1:{port}"));
    }

    private static async Task WaitForServerAsync(Process server, Uri uri)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        while (!timeout.IsCancellationRequested)
        {
            if (server.HasExited)
                throw new InvalidOperationException("The laboratory server exited before health became available.");
            try
            {
                using HttpClient client = new();
                using HttpResponseMessage response = await client.GetAsync(new Uri(uri, "/health"), timeout.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(100, timeout.Token).ConfigureAwait(false);
        }
        throw new TimeoutException("The laboratory server did not pass its health check.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rowles.Morphogenesis.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("The Morphogenesis repository root could not be located.");
    }

    private static async Task WaitForTextAsync(ILocator locator, string expected)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        while (!timeout.IsCancellationRequested)
        {
            if (StringComparer.Ordinal.Equals((await locator.TextContentAsync().ConfigureAwait(false))?.Trim(), expected))
                return;
            await Task.Delay(50, timeout.Token).ConfigureAwait(false);
        }
        throw new TimeoutException($"Expected browser text '{expected}'.");
    }

    private static async Task WaitForAttributeAsync(ILocator locator, string name)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (!timeout.IsCancellationRequested)
        {
            if (await locator.GetAttributeAsync(name).ConfigureAwait(false) is not null)
                return;
            await Task.Delay(50, timeout.Token).ConfigureAwait(false);
        }
        throw new TimeoutException($"The canvas did not set {name}.");
    }
}
