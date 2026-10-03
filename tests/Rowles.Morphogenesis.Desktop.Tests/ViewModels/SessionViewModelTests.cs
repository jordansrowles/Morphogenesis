using Rowles.Morphogenesis.Desktop.Networking;
using Rowles.Morphogenesis.Desktop.Rendering;
using Rowles.Morphogenesis.Desktop.Tests.Networking;
using Rowles.Morphogenesis.Desktop.Tests.Testing;
using Rowles.Morphogenesis.Desktop.ViewModels;
using Xunit;

namespace Rowles.Morphogenesis.Desktop.Tests.ViewModels;

public sealed class SessionViewModelTests
{
    [Fact]
    public async Task ControlsFollowSessionStateAndCommandsUseFreshIdsAndCurrentRevision()
    {
        SessionDto session = LaboratoryApiClientTests.CreateSession();
        FakeLaboratoryApiClient api = new(session);
        await using SessionViewModel viewModel = new(session, api, new BlockingLiveFrameStreamClient());

        Assert.True(viewModel.CanStart);
        Assert.False(viewModel.CanPause);
        Assert.True(viewModel.CanStop);

        await viewModel.StartCommand.ExecuteAsync(null);

        Assert.Single(api.Commands);
        Assert.Equal("start", api.Commands[0].Command);
        Assert.NotEqual(Guid.Empty, api.Commands[0].Request.CommandId);
        Assert.Equal(0, api.Commands[0].Request.ExpectedRevision);
        Assert.True(viewModel.CanPause);
        Assert.Equal(SessionStatusDto.Running, viewModel.Session.Status);

        await viewModel.PauseCommand.ExecuteAsync(null);
        Assert.Equal(1, api.Commands[1].Request.ExpectedRevision);
        Assert.NotEqual(api.Commands[0].Request.CommandId, api.Commands[1].Request.CommandId);
        Assert.True(viewModel.CanResume);
        Assert.True(viewModel.CanStep);
    }

    [Fact]
    public async Task ConflictReplacesAuthoritativeStateAndDoesNotRetry()
    {
        SessionDto initial = LaboratoryApiClientTests.CreateSession();
        FakeLaboratoryApiClient api = new(initial)
        {
            CommandHandler = (_, _) => new CommandResponse(
                initial with { Status = SessionStatusDto.Paused, Revision = 8, CurrentMcs = 4 },
                Conflict: true,
                NotFound: false)
        };
        await using SessionViewModel viewModel = new(initial, api, new BlockingLiveFrameStreamClient());

        await viewModel.StartCommand.ExecuteAsync(null);

        Assert.Single(api.Commands);
        Assert.Equal(SessionStatusDto.Paused, viewModel.Session.Status);
        Assert.Equal(8, viewModel.Session.Revision);
        Assert.Contains("Conflict", viewModel.CommandStatus, StringComparison.Ordinal);
        Assert.True(viewModel.CanResume);
    }

    [Fact]
    public async Task ReconnectRefreshesStateAndDisconnectDoesNotSendStop()
    {
        SessionDto running = LaboratoryApiClientTests.CreateSession() with { Status = SessionStatusDto.Running, Revision = 4 };
        FakeLaboratoryApiClient api = new(running);
        BlockingLiveFrameStreamClient stream = new();
        await using SessionViewModel viewModel = new(running, api, stream);

        await viewModel.OpenAsync();
        await stream.WaitForConnectionsAsync(1).WaitAsync(TimeSpan.FromSeconds(2));
        await viewModel.ReconnectCommand.ExecuteAsync(null);
        await stream.WaitForConnectionsAsync(2).WaitAsync(TimeSpan.FromSeconds(2));
        await viewModel.DisconnectStreamAsync();

        Assert.True(api.GetSessionCalls >= 3);
        Assert.Empty(api.Commands);
        Assert.Equal(SessionStatusDto.Running, viewModel.Session.Status);
    }

    [Fact]
    public async Task CellInspectorReportsTheServerInspectionAndItsFrameAge()
    {
        SessionDto session = LaboratoryApiClientTests.CreateSession();
        FakeLaboratoryApiClient api = new(session);
        CellInspectorViewModel inspector = new(api, session.SessionId);

        await inspector.InspectAsync(2, displayedFrameMcs: 3);

        Assert.Equal(2, inspector.Inspection!.CellId);
        Assert.Equal("Type A", inspector.Inspection.CellTypeName);
        Assert.Contains("Inspection at MCS 0", inspector.Status, StringComparison.Ordinal);
        Assert.Contains("displayed frame is MCS 3", inspector.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowShowsTheRequiredWarningForRemoteHttpServers()
    {
        SessionDto session = LaboratoryApiClientTests.CreateSession();
        FakeLaboratoryApiClient api = new(session);
        MainWindowViewModel viewModel = new(api,
            new Rowles.Morphogenesis.Desktop.Configuration.DesktopPreferences("http://192.168.1.20:5080"),
            new BlockingLiveFrameStreamClient());

        Assert.Equal("This server has no authentication. Use only on a trusted network.", viewModel.NetworkWarning);
    }
}
