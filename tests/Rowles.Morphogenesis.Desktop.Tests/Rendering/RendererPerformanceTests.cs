using System.Diagnostics;
using Rowles.Morphogenesis.Desktop.Networking;
using Rowles.Morphogenesis.Desktop.Rendering;
using Rowles.Morphogenesis.Desktop.Tests.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Rowles.Morphogenesis.Desktop.Tests.Rendering;

public sealed class RendererPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Performance")]
    public async Task RendererSustainsTheDeclaredFrameRatesWithoutHeapGrowthOrPerSiteObjects()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ROWLES_MORPHOGENESIS_DESKTOP_PERF"), "1", StringComparison.Ordinal))
            return;

        RunResult[] results = await AvaloniaTestHost.RunAsync(() => Task.FromResult(new[]
        {
            RunScenario(256, 20, LatticeViewMode.CellIdentity),
            RunScenario(512, 10, LatticeViewMode.CellIdentity),
            RunScenario(256, 10, LatticeViewMode.Boundaries)
        }));

        foreach (RunResult result in results)
        {
            output.WriteLine($"{result.Width}x{result.Height} {result.Mode}: {result.Frames} frames in {result.Elapsed.TotalSeconds:F2}s, " +
                             $"{result.Frames / result.Elapsed.TotalSeconds:F2} FPS, " +
                             $"{result.AllocatedBytes / (double)result.Frames:F2} allocated bytes/frame, " +
                             $"heap delta {result.HeapDelta:N0} bytes.");
            Assert.True(result.Frames / result.Elapsed.TotalSeconds >= result.TargetFps,
                $"{result.Width}x{result.Height} {result.Mode} did not sustain {result.TargetFps} FPS.");
            Assert.True(result.AllocatedBytes / (double)result.Frames / (result.Width * result.Height) < sizeof(int),
                $"{result.Width}x{result.Height} {result.Mode} allocated at least four bytes per site per frame.");
            Assert.InRange(result.HeapDelta, long.MinValue, 4 * 1024 * 1024);
        }
    }

    private static RunResult RunScenario(int size, int targetFps, LatticeViewMode mode)
    {
        int siteCount = checked(size * size);
        int[] cellIds = new int[siteCount];
        for (int index = 0; index < cellIds.Length; index++)
            cellIds[index] = index % 73 == 0 ? 0 : index % 127 + 1;
        int[] types = new int[129];
        for (int index = 1; index < types.Length; index++)
            types[index] = index % 2 + 1;
        SessionStaticMetadataDto metadata = new("Periodic", types);
        FullFrameBuffer frame = FullFrameProtocol.Decode(FullFrameProtocol.Encode(1, 1, size, size, cellIds));

        using LatticeBitmapRenderer renderer = new();
        for (int warmup = 0; warmup < 20; warmup++)
            renderer.Render(frame, metadata, mode);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long heapBefore = GC.GetTotalMemory(forceFullCollection: true);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long frames = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();
        long nextSample = Stopwatch.Frequency * 5;
        List<long> heapSamples = [];

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(60))
        {
            renderer.Render(frame, metadata, mode);
            frames++;
            if (stopwatch.ElapsedTicks >= nextSample)
            {
                heapSamples.Add(GC.GetTotalMemory(forceFullCollection: false));
                nextSample += Stopwatch.Frequency * 5;
            }
        }

        stopwatch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        long heapAfter = GC.GetTotalMemory(forceFullCollection: true);
        long delta = heapAfter - heapBefore;
        if (heapSamples.Count >= 2)
            Assert.True(heapSamples[^1] <= heapSamples[0] + 8 * 1024 * 1024,
                $"Managed heap grew throughout the {size}x{size} {mode} run.");
        return new RunResult(size, size, targetFps, mode, frames, stopwatch.Elapsed, allocated, delta);
    }

    private sealed record RunResult(int Width, int Height, int TargetFps, LatticeViewMode Mode,
        long Frames, TimeSpan Elapsed, long AllocatedBytes, long HeapDelta);
}
