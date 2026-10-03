using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Server.Persistence;
using Rowles.Morphogenesis.Server.Tests.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Rowles.Morphogenesis.Server.Tests.Persistence;

public sealed class SqliteWriterLoadTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SequentialFrameWritesStayWithinTheLatencyGates()
    {
        using TemporaryLaboratoryRoot root = new();
        using LaboratoryFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        LaboratoryDatabase database = factory.Services.GetRequiredService<LaboratoryDatabase>();
        SqliteWriteQueue queue = factory.Services.GetRequiredService<SqliteWriteQueue>();
        Guid sessionId = await CreateRecordingRunAsync(database);

        const int FrameCount = 128;
        double[] elapsedMilliseconds = new double[FrameCount];
        for (int index = 0; index < FrameCount; index++)
        {
            elapsedMilliseconds[index] = await InsertFrameAsync(database, sessionId, index);
        }

        double[] sorted = elapsedMilliseconds.Order().ToArray();
        double median = sorted[sorted.Length / 2];
        double p95 = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1];
        Assert.Equal(0, queue.QueueDepth);
        Assert.True(median < 20, $"SQLite median frame-write latency was {median:F3} ms; required < 20 ms.");
        Assert.True(p95 < 50, $"SQLite p95 frame-write latency was {p95:F3} ms; required < 50 ms.");
        output.WriteLine($"SQLite sequential frame writes: {FrameCount} frames, median {median:F3} ms, p95 {p95:F3} ms, final queue depth {queue.QueueDepth}.");
    }

    [Fact]
    public async Task ConcurrentFrameInsertBurstDrainsWithoutBusyErrors()
    {
        using TemporaryLaboratoryRoot root = new();
        using LaboratoryFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        LaboratoryDatabase database = factory.Services.GetRequiredService<LaboratoryDatabase>();
        SqliteWriteQueue queue = factory.Services.GetRequiredService<SqliteWriteQueue>();
        Guid sessionId = await CreateRecordingRunAsync(database);

        const int FrameCount = 256;
        long nextSequence = -1;
        Task<double>[] writes = Enumerable.Range(0, FrameCount)
            .Select(_ => InsertFrameAsync(database, sessionId, checked((int)Interlocked.Increment(ref nextSequence))))
            .ToArray();
        double[] elapsedMilliseconds = await Task.WhenAll(writes);
        using CancellationTokenSource drainTimeout = new(TimeSpan.FromSeconds(10));
        while (queue.QueueDepth != 0)
            await Task.Delay(1, drainTimeout.Token);

        double[] sorted = elapsedMilliseconds.Order().ToArray();
        double median = sorted[sorted.Length / 2];
        double p95 = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1];
        long frameRows = await queue.ExecuteAsync(async (connection, cancellationToken) =>
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Frames WHERE RunId = $id;";
            command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        });

        Assert.Equal(FrameCount, frameRows);
        Assert.Equal(0, queue.QueueDepth);
        output.WriteLine($"SQLite concurrent insert burst: {FrameCount} frames, queued call-completion median {median:F3} ms, p95 {p95:F3} ms, final queue depth {queue.QueueDepth}.");
    }

    private static async Task<Guid> CreateRecordingRunAsync(LaboratoryDatabase database)
    {
        ExperimentManifest manifest = ExperimentManifest.ReadJson(
            Path.Combine(AppContext.BaseDirectory, "experiments", "canonical", "E02-control.json"));
        await using SimulationSession session = SimulationSessionFactory.Create(manifest, replicateIndex: 0);
        RecordingSettings settings = new(
            true,
            5,
            20,
            0.75,
            RecordingFormat.EnvelopeVersion,
            RecordingFormat.KeyframeCodecVersion,
            RecordingFormat.DeltaCodecVersion,
            RecordingFormat.Compression);
        await database.CreateRunAsync(
            manifest,
            session.Metadata,
            session.GetSnapshot(),
            settings,
            DateTimeOffset.UtcNow);
        return session.Metadata.SessionId;
    }

    private static async Task<double> InsertFrameAsync(LaboratoryDatabase database, Guid sessionId, int sequence)
    {
        byte[] envelope = [0x91, 0x01, 0x02, 0x03, 0x04];
        RecordingFrameKind kind = sequence == 0 ? RecordingFrameKind.Keyframe : RecordingFrameKind.Delta;
        EncodedRecordingFrame frame = new(
            sequence,
            checked(sequence * 5),
            kind,
            RecordingFormat.EnvelopeVersion,
            kind == RecordingFrameKind.Keyframe ? RecordingFormat.KeyframeCodecVersion : RecordingFormat.DeltaCodecVersion,
            RecordingFormat.Compression,
            envelope,
            envelope.Length);
        long started = Stopwatch.GetTimestamp();
        await database.AppendFrameAsync(sessionId, frame).ConfigureAwait(false);
        return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
}
