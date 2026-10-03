using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Laboratory.Playback;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;

namespace Rowles.Morphogenesis.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class RecordingAccumulatorBenchmarks
{
    private CoalescingLatticeChangeAccumulator _accumulator = null!;

    [Params(64, 128, 256)]
    public int SiteCount { get; set; }

    [GlobalSetup]
    public void Setup() => _accumulator = new CoalescingLatticeChangeAccumulator(SiteCount);

    [Benchmark(OperationsPerInvoke = 1000)]
    public int AcceptedCopies()
    {
        for (int index = 0; index < 1000; index++)
        {
            _accumulator.AcceptedCopy(index % SiteCount, oldCellId: 0, newCellId: index & 7);
        }

        return _accumulator.Count;
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class RecordingDeltaEncodingBenchmarks
{
    private int[] _indices = null!;
    private int[] _cellIds = null!;
    private byte[] _destination = null!;

    [Params(1, 10, 50, 75, 100)]
    public int ChangedPercent { get; set; }

    private int SiteCount => 256 * 256;

    [GlobalSetup]
    public void Setup()
    {
        int count = SiteCount * ChangedPercent / 100;
        _indices = new int[count];
        _cellIds = new int[count];
        for (int item = 0; item < count; item++)
        {
            _indices[item] = (int)((long)item * SiteCount / count);
            _cellIds[item] = 16_384;
        }

        _destination = new byte[DeltaPayloadCodec.GetBufferSize(SiteCount)];
    }

    [Benchmark]
    public int EncodeSortedDelta() => DeltaPayloadCodec.Encode(_indices, _cellIds, SiteCount, _destination);
}

[MemoryDiagnoser]
[ShortRunJob]
public class RecordingKeyframeBenchmarks
{
    private int[] _source = null!;
    private int[] _destination = null!;
    private byte[] _payload = null!;

    [Params(64, 128, 256, 512)]
    public int Width { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        int siteCount = checked(Width * Width);
        _source = new int[siteCount];
        _destination = new int[siteCount];
        for (int index = 0; index < siteCount; index++)
        {
            _source[index] = index % 257;
        }

        _payload = new byte[KeyframePayloadCodec.GetPayloadSize(siteCount)];
        KeyframePayloadCodec.Encode(_source, _payload);
    }

    [Benchmark]
    public int Encode() => KeyframePayloadCodec.Encode(_source, _payload);

    [Benchmark]
    public int Decode()
    {
        KeyframePayloadCodec.Decode(_payload, _destination);
        return _destination[Width * Width - 1];
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class RecordingEnvelopeBenchmarks
{
    private RecordingMessagePackCodec _codec = null!;
    private RecordedFrameEnvelope _envelope = null!;
    private byte[] _encoded = null!;

    [GlobalSetup]
    public void Setup()
    {
        const int Width = 256;
        byte[] payload = new byte[KeyframePayloadCodec.GetPayloadSize(Width * Width)];
        for (int index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)(index % 17);
        }

        _envelope = new RecordedFrameEnvelope(
            RecordingFormat.EnvelopeVersion,
            10,
            50,
            RecordingFrameKind.Keyframe,
            RecordingFormat.KeyframeCodecVersion,
            RecordingFormat.Compression,
            Width,
            Width,
            payload.Length,
            payload);
        _codec = new RecordingMessagePackCodec(Width * Width);
        using PooledEnvelopeBuffer serialized = _codec.Serialize(_envelope);
        _encoded = serialized.Bytes.ToArray();
    }

    [Benchmark]
    public int SerializeLz4Envelope()
    {
        using PooledEnvelopeBuffer serialized = _codec.Serialize(_envelope);
        return serialized.WrittenCount;
    }

    [Benchmark]
    public int DeserializeLz4Envelope() => RecordingMessagePackCodec.Deserialize(_encoded).Payload.Length;
}

[MemoryDiagnoser]
[ShortRunJob]
public class RecordingSeekBenchmarks
{
    private RecordedSimulationSource _source = null!;
    private long _targetMcs;

    [Params(1, 5, 10, 19)]
    public int DeltaCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        const int Width = 64;
        const int Height = 64;
        ExperimentManifest manifest = ExperimentManifest.ReadJson(
            Path.Combine(AppContext.BaseDirectory, "Scenarios", "E02-control.json")) with
        {
            GridWidth = Width,
            GridHeight = Height,
            McsCount = DeltaCount * 5
        };
        SimulationSession metadataSession = SimulationSessionFactory.Create(manifest, replicateIndex: 0);
        Guid sessionId = metadataSession.Metadata.SessionId;
        SimulationMetadata metadata = metadataSession.Metadata;
        metadataSession.DisposeAsync().AsTask().GetAwaiter().GetResult();
        RecordingHeader header = new(
            RecordingFormat.SchemaVersion,
            sessionId,
            metadata.RunIdentity,
            manifest.ToJson(),
            DateTimeOffset.UnixEpoch,
            Width,
            Height,
            5,
            20,
            0.75,
            RecordingFormat.EnvelopeVersion,
            RecordingFormat.KeyframeCodecVersion,
            RecordingFormat.DeltaCodecVersion,
            RecordingFormat.Compression,
            metadata);

        int siteCount = Width * Height;
        int[] lattice = new int[siteCount];
        Array.Fill(lattice, 1);
        BenchmarkRecordingReader reader = new(header, lattice, DeltaCount);
        _targetMcs = DeltaCount * 5L;
        _source = RecordedSimulationSource.OpenAsync(reader, sessionId).AsTask().GetAwaiter().GetResult();
    }

    [Benchmark]
    public long SeekAcrossDeltas()
    {
        _source.SeekAsync(_targetMcs).AsTask().GetAwaiter().GetResult();
        return _source.CurrentMcs;
    }

    [GlobalCleanup]
    public void Cleanup() => _source.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private sealed class BenchmarkRecordingReader : IRecordingReader
    {
        private readonly RecordingHeader _header;
        private readonly RecordingFrameIndexEntry[] _index;
        private readonly EncodedRecordingFrame[] _frames;

        internal BenchmarkRecordingReader(RecordingHeader header, int[] initialLattice, int deltaCount)
        {
            _header = header;
            _frames = new EncodedRecordingFrame[deltaCount + 1];
            _frames[0] = EncodeFrame(0, 0, RecordingFrameKind.Keyframe, initialLattice, [], []);
            _index = new RecordingFrameIndexEntry[deltaCount + 1];
            _index[0] = new RecordingFrameIndexEntry(0, 0, RecordingFrameKind.Keyframe);
            for (int delta = 1; delta <= deltaCount; delta++)
            {
                long mcs = delta * 5L;
                _frames[delta] = EncodeFrame(delta, mcs, RecordingFrameKind.Delta, initialLattice, [delta], [1 + (delta % 2)]);
                _index[delta] = new RecordingFrameIndexEntry(delta, mcs, RecordingFrameKind.Delta);
            }
        }

        public ValueTask<RecordingHeader> GetHeaderAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_header);

        public ValueTask<IReadOnlyList<RecordingFrameIndexEntry>> GetFrameIndexAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<RecordingFrameIndexEntry>>(_index);

        public ValueTask<EncodedRecordingFrame> ReadFrameAsync(Guid sessionId, long sequence, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_frames[checked((int)sequence)]);

        public ValueTask<RecordingFrameIndexEntry?> FindFrameAtOrBeforeAsync(
            Guid sessionId,
            long mcs,
            CancellationToken cancellationToken = default)
        {
            for (int index = _index.Length - 1; index >= 0; index--)
            {
                if (_index[index].Mcs <= mcs)
                    return ValueTask.FromResult<RecordingFrameIndexEntry?>(_index[index]);
            }

            return ValueTask.FromResult<RecordingFrameIndexEntry?>(null);
        }

        public ValueTask<RecordingFrameIndexEntry?> FindNearestKeyframeAtOrBeforeAsync(
            Guid sessionId,
            long mcs,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<RecordingFrameIndexEntry?>(_index[0]);

        private static EncodedRecordingFrame EncodeFrame(
            long sequence,
            long mcs,
            RecordingFrameKind kind,
            int[] lattice,
            int[] indices,
            int[] cellIds)
        {
            byte[] rawPayload;
            int codecVersion;
            if (kind == RecordingFrameKind.Keyframe)
            {
                rawPayload = new byte[KeyframePayloadCodec.GetPayloadSize(lattice.Length)];
                KeyframePayloadCodec.Encode(lattice, rawPayload);
                codecVersion = RecordingFormat.KeyframeCodecVersion;
            }
            else
            {
                byte[] scratch = new byte[DeltaPayloadCodec.GetBufferSize(lattice.Length)];
                int written = DeltaPayloadCodec.Encode(indices, cellIds, lattice.Length, scratch);
                rawPayload = scratch.AsSpan(0, written).ToArray();
                codecVersion = RecordingFormat.DeltaCodecVersion;
            }

            RecordedFrameEnvelope envelope = new(
                RecordingFormat.EnvelopeVersion,
                sequence,
                mcs,
                kind,
                codecVersion,
                RecordingFormat.Compression,
                64,
                64,
                rawPayload.Length,
                rawPayload);
            using PooledEnvelopeBuffer encoded = new RecordingMessagePackCodec(lattice.Length).Serialize(envelope);
            return new EncodedRecordingFrame(
                sequence,
                mcs,
                kind,
                RecordingFormat.EnvelopeVersion,
                codecVersion,
                RecordingFormat.Compression,
                encoded.Bytes.ToArray(),
                rawPayload.Length);
        }
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class RecordingEnabledSessionBenchmarks
{
    private static readonly ExperimentManifest Manifest = CreateManifest();

    [Params(false, true)]
    public bool RecordingEnabled { get; set; }

    [Benchmark]
    public async Task<SimulationOperationalCounters> RunSession()
    {
        await using SimulationSession session = SimulationSessionFactory.Create(
            Manifest,
            replicateIndex: 0,
            recordingOptions: new RecordingOptions { Enabled = RecordingEnabled },
            recordingStore: RecordingEnabled ? NoOpRecordingStore.Instance : null);
        await session.StartAsync(Guid.NewGuid(), expectedRevision: 0).ConfigureAwait(false);
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        while (session.GetSnapshot().Status != SimulationSessionStatus.Completed)
        {
            await Task.Delay(1, timeout.Token).ConfigureAwait(false);
        }

        while (session.GetSnapshot().RecordingState == RecordingState.Active)
        {
            await Task.Delay(1, timeout.Token).ConfigureAwait(false);
        }

        SimulationSessionSnapshot completed = session.GetSnapshot();
        if (completed.Status != SimulationSessionStatus.Completed ||
            (RecordingEnabled && completed.RecordingState != RecordingState.Completed))
        {
            throw new InvalidOperationException(
                $"The benchmark run ended in {completed.Status}/{completed.RecordingState}: {completed.RecordingFailure}");
        }

        return completed.Counters;
    }

    private static ExperimentManifest CreateManifest()
    {
        ExperimentManifest source = ExperimentManifest.ReadJson(
            Path.Combine(AppContext.BaseDirectory, "Scenarios", "E02-control.json"));
        return source with
        {
            GridWidth = 128,
            GridHeight = 128,
            McsCount = 64,
            ReplicateCount = 1,
            Measurements = source.Measurements with
            {
                EveryMcs = 64,
                IncludeMcsZero = false,
                ValidateInvariantsEveryMcs = 0
            }
        };
    }

    private sealed class NoOpRecordingStore : IRecordingStore
    {
        internal static NoOpRecordingStore Instance { get; } = new();

        public ValueTask CreateAsync(RecordingHeader header, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask AppendFrameAsync(Guid sessionId, EncodedRecordingFrame frame, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask CompleteAsync(Guid sessionId, RecordingCompletion completion, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
