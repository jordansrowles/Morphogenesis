using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Server.Persistence;

namespace Rowles.Morphogenesis.Server.Sessions;

public sealed record SessionDto(
    Guid SessionId,
    string ExperimentId,
    string ReplicateId,
    int ReplicateIndex,
    SimulationSessionStatus Status,
    long Revision,
    long CurrentMcs,
    long TargetMcs,
    int Width,
    int Height,
    string KernelId,
    RecordingState RecordingState,
    string? RecordingFailure,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc)
{
    public SessionStaticMetadataDto? Metadata { get; init; }

    internal static SessionDto From(PersistedRun run, SimulationSessionSnapshot? liveSnapshot = null) => new(
        run.Id,
        run.ExperimentId,
        run.ReplicateId,
        run.ReplicateIndex,
        liveSnapshot?.Status ?? run.Status,
        liveSnapshot?.Revision ?? run.Revision,
        liveSnapshot?.CurrentMcs ?? run.CurrentMcs,
        ExperimentManifestMcs(run.ManifestJson),
        run.Width,
        run.Height,
        run.KernelId,
        liveSnapshot?.RecordingState ?? run.RecordingState,
        liveSnapshot?.RecordingFailure ?? run.RecordingFailure,
        run.CreatedAtUtc,
        run.StartedAtUtc,
        run.CompletedAtUtc)
    {
        Metadata = CreateMetadata(run)
    };

    internal static SessionDto From(
        PersistedRun run,
        SimulationSessionSnapshot snapshot,
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? completedAtUtc) => new(
            run.Id,
            run.ExperimentId,
            run.ReplicateId,
            run.ReplicateIndex,
            snapshot.Status,
            snapshot.Revision,
            snapshot.CurrentMcs,
            ExperimentManifestMcs(run.ManifestJson),
            run.Width,
            run.Height,
            run.KernelId,
            snapshot.RecordingState,
            snapshot.RecordingFailure,
            run.CreatedAtUtc,
        startedAtUtc ?? run.StartedAtUtc,
        completedAtUtc ?? run.CompletedAtUtc)
    {
        Metadata = CreateMetadata(run)
    };

    private static SessionStaticMetadataDto CreateMetadata(PersistedRun run) => new(
        run.Payload.Metadata.BoundaryMode.ToString(),
        (int[])run.Payload.Metadata.CellTypeByCellId.Clone());

    private static long ExperimentManifestMcs(string manifestJson) =>
        Rowles.Morphogenesis.Experiments.ExperimentManifest.FromJson(manifestJson).McsCount;
}

public sealed record SessionStaticMetadataDto(
    string BoundaryMode,
    int[] CellTypeByCellId);

public sealed record RecordingFrameDto(long Sequence, long Mcs, RecordingFrameKind Kind);

public sealed record RecordingDto(
    Guid SessionId,
    RecordingState RecordingState,
    string? RecordingFailure,
    int RecordingSchemaVersion,
    int RecordEveryMcs,
    int KeyframeEveryRecordedFrames,
    string Compression,
    long? FirstMcs,
    long? LastMcs,
    int FrameCount,
    IReadOnlyList<RecordingFrameDto> Frames);
