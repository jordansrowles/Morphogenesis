using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;
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
