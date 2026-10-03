using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Data.Sqlite;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Laboratory.Playback;
using Rowles.Morphogenesis.Laboratory.Publication;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Server.Api;
using Rowles.Morphogenesis.Server.Configuration;
using Rowles.Morphogenesis.Server.Diagnostics;
using Rowles.Morphogenesis.Server.Persistence;

namespace Rowles.Morphogenesis.Benchmarks.Laboratory;

[MemoryDiagnoser]
[ShortRunJob]
public class SqliteRecordingFrameInsertBenchmarks
{
    private const int FramesPerBurst = 256;
    private string _root = null!;
    private LaboratoryDatabase _database = null!;
    private SqliteWriteQueue _writeQueue = null!;
    private Guid _sessionId;
    private long _sequence;
    private readonly byte[] _payload = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();

    [GlobalSetup]
    public async Task CreateDatabase()
    {
        _root = Path.Combine(Path.GetTempPath(), "morphogenesis-sqlite-benchmark", Guid.NewGuid().ToString("N"));
        string data = Path.Combine(_root, "data");
        string logs = Path.Combine(_root, "logs");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(logs);
        LaboratoryServerOptions options = new()
        {
            DataDirectory = data,
            LogDirectory = logs
        };
        LaboratoryDiagnostics diagnostics = new();
        _writeQueue = new SqliteWriteQueue(options, diagnostics);
        await _writeQueue.StartAsync(CancellationToken.None).ConfigureAwait(false);
        _database = new LaboratoryDatabase(options, _writeQueue, diagnostics);
        await _database.InitializeAsync().ConfigureAwait(false);

        ExperimentManifest manifest = ExperimentManifest.ReadJson(
            Path.Combine(AppContext.BaseDirectory, "Scenarios", "E02-control.json"));
        await using SimulationSession session = SimulationSessionFactory.Create(manifest, replicateIndex: 0);
        _sessionId = session.Metadata.SessionId;
        RecordingSettings recording = new(
            true,
            5,
            20,
            0.75,
            RecordingFormat.EnvelopeVersion,
            RecordingFormat.KeyframeCodecVersion,
            RecordingFormat.DeltaCodecVersion,
            RecordingFormat.Compression);
        await _database.CreateRunAsync(
            manifest,
            session.Metadata,
            session.GetSnapshot(),
            recording,
            DateTimeOffset.UtcNow).ConfigureAwait(false);
    }

    [Benchmark(OperationsPerInvoke = FramesPerBurst)]
    public async Task<long> InsertSustainedFrameBurst()
    {
        Task[] writes = new Task[FramesPerBurst];
        long lastSequence = 0;
        for (int index = 0; index < writes.Length; index++)
        {
            long sequence = Interlocked.Increment(ref _sequence) - 1;
            lastSequence = sequence;
            EncodedRecordingFrame frame = new(
                sequence,
                checked(sequence * 5),
                RecordingFrameKind.Delta,
                RecordingFormat.EnvelopeVersion,
                RecordingFormat.DeltaCodecVersion,
                RecordingFormat.Compression,
                _payload,
                _payload.Length);
            writes[index] = _database.AppendFrameAsync(_sessionId, frame);
        }

        await Task.WhenAll(writes).ConfigureAwait(false);
        return lastSequence;
    }

    [GlobalCleanup]
    public async Task DisposeDatabase()
    {
        await _writeQueue.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await _writeQueue.DisposeAsync().ConfigureAwait(false);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class RecordingFrameReconstructionBenchmarks
{
    private string _root = null!;
    private LaboratoryDatabase _database = null!;
    private SqliteWriteQueue _writeQueue = null!;
    private SqliteRecordingReader _reader = null!;
    private RecordingFrameReconstructor _reconstructor = null!;
    private Guid _sessionId;

    [Params(256, 512)]
    public int Width { get; set; }

    [Params(1, 4)]
    public int ConcurrentRequests { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "morphogenesis-frame-reconstruction-benchmark", Guid.NewGuid().ToString("N"));
        string dataDirectory = Path.Combine(_root, "data");
        string logDirectory = Path.Combine(_root, "logs");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(logDirectory);
        LaboratoryServerOptions options = new() { DataDirectory = dataDirectory, LogDirectory = logDirectory };
        LaboratoryDiagnostics diagnostics = new();
        _writeQueue = new SqliteWriteQueue(options, diagnostics);
        await _writeQueue.StartAsync(CancellationToken.None).ConfigureAwait(false);
        _database = new LaboratoryDatabase(options, _writeQueue, diagnostics);
        await _database.InitializeAsync().ConfigureAwait(false);

        ExperimentManifest manifest = ExperimentManifest.ReadJson(
            Path.Combine(AppContext.BaseDirectory, "Scenarios", "E02-control.json")) with
        {
            GridWidth = Width,
            GridHeight = Width,
            McsCount = 5
        };
        await using SimulationSession metadataSession = SimulationSessionFactory.Create(manifest, replicateIndex: 0);
        _sessionId = metadataSession.Metadata.SessionId;
        await using IAsyncEnumerator<SimulationFrameLease> frames =
            metadataSession.WatchFramesAsync().GetAsyncEnumerator();
        if (!await frames.MoveNextAsync().ConfigureAwait(false))
            throw new InvalidOperationException("The benchmark session did not publish its initial frame.");
        using SimulationFrameLease initialFrame = frames.Current;
        int[] initialCellIds = initialFrame.CellIds.ToArray();

        RecordingHeader header = new(
            RecordingFormat.SchemaVersion,
            _sessionId,
            metadataSession.Metadata.RunIdentity,
            manifest.ToJson(),
            DateTimeOffset.UnixEpoch,
            Width,
            Width,
            5,
            20,
            0.75,
            RecordingFormat.EnvelopeVersion,
            RecordingFormat.KeyframeCodecVersion,
            RecordingFormat.DeltaCodecVersion,
            RecordingFormat.Compression,
            metadataSession.Metadata);
        RecordingSettings recording = new(
            true,
            5,
            20,
            0.75,
            RecordingFormat.EnvelopeVersion,
            RecordingFormat.KeyframeCodecVersion,
            RecordingFormat.DeltaCodecVersion,
            RecordingFormat.Compression);
        await _database.CreateRunAsync(
            manifest,
            metadataSession.Metadata,
            metadataSession.GetSnapshot(),
            recording,
            DateTimeOffset.UtcNow).ConfigureAwait(false);

        byte[] keyframePayload = new byte[KeyframePayloadCodec.GetPayloadSize(initialCellIds.Length)];
        int keyframePayloadSize = KeyframePayloadCodec.Encode(initialCellIds, keyframePayload);
        await _database.AppendFrameAsync(
            _sessionId,
            EncodeFrame(header, 0, 0, RecordingFrameKind.Keyframe, keyframePayload.AsSpan(0, keyframePayloadSize).ToArray())).ConfigureAwait(false);

        byte[] deltaPayload = new byte[DeltaPayloadCodec.GetBufferSize(initialCellIds.Length)];
        int deltaPayloadSize = DeltaPayloadCodec.Encode([initialCellIds.Length - 1], [2], initialCellIds.Length, deltaPayload);
        await _database.AppendFrameAsync(
            _sessionId,
            EncodeFrame(header, 1, 5, RecordingFrameKind.Delta, deltaPayload.AsSpan(0, deltaPayloadSize).ToArray())).ConfigureAwait(false);

        _reader = new SqliteRecordingReader(_database);
        _reconstructor = new RecordingFrameReconstructor(maximumConcurrentReconstructions: 2, maximumSiteCount: Width * Width);
    }

    [Benchmark]
    public async Task<long> ReconstructAndEncodeConcurrentFrames()
    {
        Task<long>[] requests = new Task<long>[ConcurrentRequests];
        for (int index = 0; index < requests.Length; index++)
            requests[index] = ReconstructAndEncodeOneAsync();
        return (await Task.WhenAll(requests).ConfigureAwait(false)).Sum();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_writeQueue is null)
            return;

        await _writeQueue.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await _writeQueue.DisposeAsync().ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<long> ReconstructAndEncodeOneAsync()
    {
        using ReconstructedRecordingFrame frame = await _reconstructor.ReconstructAsync(_reader, _sessionId, 5).ConfigureAwait(false);
        byte[] response = new byte[FullFrameMessageWriter.GetMessageLength(frame.Width, frame.Height)];
        FullFrameMessageWriter.Write(response, frame.Sequence, frame.Mcs, frame.Width, frame.Height, frame.CellIds.Span);
        return frame.CellIds.Span[^1] + response[^1];
    }

    private static EncodedRecordingFrame EncodeFrame(
        RecordingHeader header,
        long sequence,
        long mcs,
        RecordingFrameKind kind,
        byte[] payload)
    {
        int codecVersion = kind == RecordingFrameKind.Keyframe
            ? RecordingFormat.KeyframeCodecVersion
            : RecordingFormat.DeltaCodecVersion;
        RecordedFrameEnvelope envelope = new(
            RecordingFormat.EnvelopeVersion,
            sequence,
            mcs,
            kind,
            codecVersion,
            RecordingFormat.Compression,
            header.Width,
            header.Height,
            payload.Length,
            payload);
        using PooledEnvelopeBuffer encoded = new RecordingMessagePackCodec(checked(header.Width * header.Height)).Serialize(envelope);
        return new EncodedRecordingFrame(
            sequence,
            mcs,
            kind,
            RecordingFormat.EnvelopeVersion,
            codecVersion,
            RecordingFormat.Compression,
            encoded.Bytes.ToArray(),
            payload.Length);
    }
}
