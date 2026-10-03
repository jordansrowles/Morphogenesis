using Rowles.Morphogenesis.Desktop.Networking;
using Rowles.Morphogenesis.Desktop.Tests.Networking;
using Rowles.Morphogenesis.Desktop.Tests.Testing;
using Rowles.Morphogenesis.Desktop.ViewModels;
using Xunit;

namespace Rowles.Morphogenesis.Desktop.Tests.ViewModels;

public sealed class RecordingPlaybackViewModelTests
{
    [Fact]
    public async Task SeekUsesServerReconstructionAndCacheNeverExceedsThreeFrames()
    {
        SessionDto session = LaboratoryApiClientTests.CreateSession() with { RecordingState = RecordingStateDto.Completed };
        FakeLaboratoryApiClient api = new(session);
        RecordingDto recording = new(session.SessionId, RecordingStateDto.Completed, null, 1, 1, 20, "lz4", 0, 40, 5,
        [
            new RecordingFrameDto(0, 0, 0),
            new RecordingFrameDto(1, 10, 1),
            new RecordingFrameDto(2, 20, 1),
            new RecordingFrameDto(3, 30, 1),
            new RecordingFrameDto(4, 40, 1)
        ]);
        List<long> displayed = [];
        await using RecordingPlaybackViewModel viewModel = new(session.SessionId, recording,
            new RecordedFrameClient(api, session.Width, session.Height), frame => displayed.Add(frame.Header.Mcs));

        await viewModel.SeekAsync(20);
        await Task.Delay(25);
        await viewModel.SeekAsync(40);
        await Task.Delay(25);

        Assert.Equal(40, viewModel.SelectedFrameMcs);
        Assert.Equal(40, displayed[^1]);
        Assert.InRange(viewModel.CachedFrameCount, 1, 3);
        Assert.Contains(20, api.RecordingFrameRequests);
        Assert.Contains(40, api.RecordingFrameRequests);
        Assert.Equal(new[] { 0.25, 0.5, 1, 2, 4 }, viewModel.PlaybackRateOptions);
        viewModel.PlaybackRate = 2;
        Assert.Equal(2, viewModel.PlaybackRate);
    }

    [Fact]
    public async Task LargeHistoricalFramesReuseBoundedReceiveAndCellBuffers()
    {
        SessionDto session = LaboratoryApiClientTests.CreateSession() with
        {
            Width = 512,
            Height = 512,
            RecordingState = RecordingStateDto.Completed
        };
        FakeLaboratoryApiClient api = new(session);
        using RecordedFrameClient client = new(api, session.Width, session.Height);
        using FullFrameBuffer first = await client.GetFrameAsync(session.SessionId, 0);
        int[] firstCells = first.CellIds;
        byte[] firstReceiveBuffer = api.RecordingFrameReceiveBuffers[^1];
        first.Dispose();

        using FullFrameBuffer second = await client.GetFrameAsync(session.SessionId, 10);

        Assert.Same(firstCells, second.CellIds);
        Assert.Same(firstReceiveBuffer, api.RecordingFrameReceiveBuffers[^1]);
        Assert.Equal(FullFrameProtocol.GetMessageLength(512, 512), api.RecordingFrameReceiveBuffers[^1].Length);
        Assert.Equal(512, second.Header.Width);
        Assert.Equal(512, second.Header.Height);
        Assert.Equal(10, second.Header.Mcs);
    }
}
