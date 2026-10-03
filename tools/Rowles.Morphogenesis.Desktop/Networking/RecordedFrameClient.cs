namespace Rowles.Morphogenesis.Desktop.Networking;

public sealed class RecordedFrameClient : IDisposable
{
    private const int MaximumConcurrentRequests = 3;
    private const int MaximumOutstandingFrames = 9;
    private readonly ILaboratoryApiClient _apiClient;
    private readonly int _width;
    private readonly int _height;
    private readonly int _messageLength;
    private readonly SemaphoreSlim _requestSlots = new(MaximumConcurrentRequests, MaximumConcurrentRequests);
    private readonly object _receiveBufferGate = new();
    private readonly Stack<byte[]> _availableReceiveBuffers = [];
    private readonly FullFrameBufferPool _frameBuffers = new(MaximumOutstandingFrames);

    public RecordedFrameClient(ILaboratoryApiClient apiClient, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        _apiClient = apiClient;
        _width = width;
        _height = height;
        _messageLength = FullFrameProtocol.GetMessageLength(width, height);
    }

    public async Task<FullFrameBuffer> GetFrameAsync(
        Guid sessionId,
        long requestedMcs,
        CancellationToken cancellationToken = default)
    {
        await _requestSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        byte[]? receiveBuffer = null;
        FullFrameBuffer? frame = null;
        try
        {
            receiveBuffer = RentReceiveBuffer();
            RecordingFrameResponse response = await _apiClient.GetRecordingFrameAsync(
                sessionId,
                requestedMcs,
                receiveBuffer.AsMemory(0, _messageLength),
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(response.Protocol, FullFrameProtocol.CurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported recording frame protocol version '{response.Protocol}'.");
            }

            if (response.PayloadLength < FullFrameProtocol.HeaderLength || response.PayloadLength > _messageLength)
                throw new InvalidDataException("The recording frame payload length is outside the expected session dimensions.");

            FullFrameHeader header = FullFrameProtocol.ReadHeader(receiveBuffer.AsSpan(0, response.PayloadLength));
            if (header.Width != _width || header.Height != _height ||
                header.Mcs != response.Mcs || header.Sequence != response.Sequence)
            {
                throw new InvalidDataException("Recording frame headers disagree with the session dimensions or HTTP response headers.");
            }

            frame = await _frameBuffers.RentAsync(header.CellCount, cancellationToken).ConfigureAwait(false);
            FullFrameHeader decoded = FullFrameProtocol.DecodeInto(receiveBuffer.AsSpan(0, response.PayloadLength), frame.CellIds);
            frame.SetHeader(decoded);
            FullFrameBuffer result = frame;
            frame = null;
            return result;
        }
        finally
        {
            frame?.Dispose();
            if (receiveBuffer is not null)
                ReturnReceiveBuffer(receiveBuffer);
            _requestSlots.Release();
        }
    }

    public void Dispose() => _requestSlots.Dispose();

    private byte[] RentReceiveBuffer()
    {
        lock (_receiveBufferGate)
        {
            if (_availableReceiveBuffers.TryPop(out byte[]? buffer))
                return buffer;
        }

        return new byte[_messageLength];
    }

    private void ReturnReceiveBuffer(byte[] buffer)
    {
        lock (_receiveBufferGate)
            _availableReceiveBuffers.Push(buffer);
    }
}
