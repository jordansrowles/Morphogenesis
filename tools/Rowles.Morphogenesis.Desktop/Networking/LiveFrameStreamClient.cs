using System.Net.WebSockets;

namespace Rowles.Morphogenesis.Desktop.Networking;

public interface ILiveFrameStreamClient
{
    Task StreamAsync(Uri streamUri, int width, int height,
        Func<FullFrameBuffer, CancellationToken, Task> onFrame, CancellationToken cancellationToken);
}

public sealed class LiveFrameStreamClient : ILiveFrameStreamClient
{
    private readonly Func<Uri, CancellationToken, Task<WebSocket>> _connectAsync;

    public LiveFrameStreamClient(Func<Uri, CancellationToken, Task<WebSocket>>? connectAsync = null)
    {
        _connectAsync = connectAsync ?? ConnectClientWebSocketAsync;
    }

    public async Task StreamAsync(
        Uri streamUri,
        int width,
        int height,
        Func<FullFrameBuffer, CancellationToken, Task> onFrame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(streamUri);
        ArgumentNullException.ThrowIfNull(onFrame);
        int messageLength = FullFrameProtocol.GetMessageLength(width, height);
        int cellCount = checked(width * height);
        byte[] message = new byte[messageLength];
        FullFrameBuffer[] slots = [new(cellCount), new(cellCount)];

        using WebSocket socket = await _connectAsync(streamUri, cancellationToken).ConfigureAwait(false);
        int slotIndex = 0;
        while (socket.State == WebSocketState.Open)
        {
            int received = await ReceiveMessageAsync(socket, message, cancellationToken).ConfigureAwait(false);
            if (received < 0)
                return;
            FullFrameBuffer slot = slots[slotIndex];
            FullFrameHeader header = FullFrameProtocol.DecodeInto(message.AsSpan(0, received), slot.CellIds);
            if (header.Width != width || header.Height != height)
                throw new InvalidDataException("The live frame dimensions differ from the session metadata.");
            slot.SetHeader(header);
            await onFrame(slot, cancellationToken).ConfigureAwait(false);
            slotIndex = (slotIndex + 1) % slots.Length;
        }
    }

    private static async Task<WebSocket> ConnectClientWebSocketAsync(Uri streamUri, CancellationToken cancellationToken)
    {
        ClientWebSocket socket = new();
        try
        {
            await socket.ConnectAsync(streamUri, cancellationToken).ConfigureAwait(false);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<int> ReceiveMessageAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (true)
        {
            if (offset == buffer.Length)
                throw new InvalidDataException("The live full-frame message exceeds the expected dimensions.");
            ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return -1;
            if (result.MessageType != WebSocketMessageType.Binary)
                throw new InvalidDataException("The live frame stream sent a non-binary WebSocket message.");
            offset += result.Count;
            if (result.EndOfMessage)
                return offset;
            if (result.Count == 0)
                throw new InvalidDataException("The live frame stream made no progress while sending a message.");
        }
    }
}
