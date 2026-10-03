using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Rowles.Morphogenesis.Desktop.Networking;

namespace Rowles.Morphogenesis.Desktop.Rendering;

public sealed class LatticeBitmapRenderer : IDisposable
{
    private byte[] _pixelBytes = [];
    private bool[] _boundaryMask = [];
    private bool _disposed;

    public WriteableBitmap? Bitmap { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public WriteableBitmap Render(FullFrameBuffer frame, SessionStaticMetadataDto? metadata, LatticeViewMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        FullFrameHeader header = frame.Header;
        int count = checked(header.Width * header.Height);
        if (header.Width <= 0 || header.Height <= 0 || header.CellCount != count || frame.CellIds.Length != count)
            throw new InvalidDataException("Frame dimensions and cell count are inconsistent.");

        EnsureStorage(header.Width, header.Height);
        if (mode == LatticeViewMode.Boundaries)
            BoundaryOverlay.FillMask(frame.CellIds, header.Width, header.Height,
                metadata?.BoundaryMode ?? "Periodic", _boundaryMask);

        int typeCount = metadata?.CellTypeByCellId.Length ?? 0;
        for (int index = 0, pixelOffset = 0; index < count; index++, pixelOffset += 4)
        {
            int cellId = frame.CellIds[index];
            RgbColour colour;
            if (mode == LatticeViewMode.Boundaries && _boundaryMask[index])
                colour = LatticePalette.Boundary;
            else if (mode == LatticeViewMode.CellType)
                colour = LatticePalette.ForType((uint)cellId < (uint)typeCount ? metadata!.CellTypeByCellId[cellId] : 0);
            else
                colour = LatticePalette.ForIdentity(cellId);

            _pixelBytes[pixelOffset] = colour.Blue;
            _pixelBytes[pixelOffset + 1] = colour.Green;
            _pixelBytes[pixelOffset + 2] = colour.Red;
            _pixelBytes[pixelOffset + 3] = byte.MaxValue;
        }

        WritePixels(Bitmap!);
        return Bitmap!;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Bitmap?.Dispose();
        Bitmap = null;
        _pixelBytes = [];
        _boundaryMask = [];
    }

    private void EnsureStorage(int width, int height)
    {
        if (Bitmap is not null && Width == width && Height == height)
            return;
        Bitmap?.Dispose();
        Width = width;
        Height = height;
        int count = checked(width * height);
        _pixelBytes = new byte[checked(count * 4)];
        _boundaryMask = new bool[count];
        Bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
    }

    private void WritePixels(WriteableBitmap bitmap)
    {
        using ILockedFramebuffer framebuffer = bitmap.Lock();
        int rowLength = checked(Width * 4);
        for (int row = 0; row < Height; row++)
            Marshal.Copy(_pixelBytes, row * rowLength, IntPtr.Add(framebuffer.Address, row * framebuffer.RowBytes), rowLength);
    }
}
