using Avalonia.Media.Imaging;
using Rowles.Morphogenesis.Desktop.Networking;
using Rowles.Morphogenesis.Desktop.Rendering;
using Rowles.Morphogenesis.Desktop.Tests.Testing;
using Xunit;

namespace Rowles.Morphogenesis.Desktop.Tests.Rendering;

public sealed class LatticeRenderingTests
{
    [Fact]
    public void IdentityAndTypeColoursAreStableAndEmptyCellsUseTheBackgroundColour()
    {
        Assert.Equal(LatticePalette.ForIdentity(21), LatticePalette.ForIdentity(21));
        Assert.Equal(LatticePalette.ForType(2), LatticePalette.ForType(2));
        Assert.Equal(new RgbColour(14, 20, 29), LatticePalette.ForIdentity(0));
        Assert.Equal(new RgbColour(14, 20, 29), LatticePalette.ForType(0));
        Assert.Equal(new RgbColour(54, 82, 196), LatticePalette.ForType(1));
    }

    [Fact]
    public void BoundaryMaskUsesFourNeighboursAndWallOrPeriodicEdges()
    {
        int[] cellIds = [1, 1, 2];
        bool[] wall = new bool[3];
        bool[] periodic = new bool[3];

        BoundaryOverlay.FillMask(cellIds, 3, 1, "Walls", wall);
        BoundaryOverlay.FillMask(cellIds, 3, 1, "Periodic", periodic);

        Assert.Equal(new[] { false, true, true }, wall);
        Assert.Equal(new[] { true, true, true }, periodic);
    }

    [Fact]
    public async Task RendererReusesBitmapUntilDimensionsChange()
    {
        await AvaloniaTestHost.RunAsync(() =>
        {
            using LatticeBitmapRenderer renderer = new();
            SessionStaticMetadataDto metadata = new("Walls", [0, 1, 2]);
            FullFrameBuffer first = FullFrameProtocol.Decode(FullFrameProtocol.Encode(0, 0, 2, 2, [0, 1, 2, 1]));
            Avalonia.Media.Imaging.WriteableBitmap bitmap = renderer.Render(first, metadata, LatticeViewMode.CellIdentity);
            Assert.Equal(2, bitmap.PixelSize.Width);
            Assert.Equal(2, bitmap.PixelSize.Height);
            Assert.Same(bitmap, renderer.Render(first, metadata, LatticeViewMode.CellType));

            FullFrameBuffer resized = FullFrameProtocol.Decode(FullFrameProtocol.Encode(1, 1, 3, 1, [1, 2, 1]));
            Avalonia.Media.Imaging.WriteableBitmap next = renderer.Render(resized, metadata, LatticeViewMode.Boundaries);
            Assert.NotSame(bitmap, next);
            Assert.Equal(3, next.PixelSize.Width);
            Assert.Equal(1, next.PixelSize.Height);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void CoordinateSelectionAccountsForZoomAndPan()
    {
        Assert.True(LatticeCoordinates.TryMapViewToCell(
            viewX: 145,
            viewY: 45,
            viewportWidth: 240,
            viewportHeight: 120,
            latticeWidth: 12,
            latticeHeight: 6,
            zoom: 2,
            panX: 25,
            panY: -5,
            out LatticeCoordinate coordinate));

        Assert.Equal(new LatticeCoordinate(3, 1), coordinate);
        Assert.False(LatticeCoordinates.TryMapViewToCell(-1, 0, 240, 120, 12, 6, 1, 0, 0, out _));
    }

}
