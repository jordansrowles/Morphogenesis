namespace Rowles.Morphogenesis.Desktop.Networking;

public sealed class RecordedFrameClient(ILaboratoryApiClient apiClient)
{
    public async Task<FullFrameBuffer> GetFrameAsync(Guid sessionId, long requestedMcs, CancellationToken cancellationToken = default)
    {
        RecordingFrameResponse response = await apiClient.GetRecordingFrameAsync(sessionId, requestedMcs, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(response.Protocol, FullFrameProtocol.CurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported recording frame protocol version '{response.Protocol}'.");
        FullFrameBuffer frame = FullFrameProtocol.Decode(response.Payload);
        if (frame.Header.Mcs != response.Mcs || frame.Header.Sequence != response.Sequence)
            throw new InvalidDataException("Recording frame headers disagree with the binary full-frame message.");
        return frame;
    }
}
