using Rowles.Morphogenesis.Experiments.Configuration;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Measurements;

namespace Rowles.Morphogenesis.Server.Persistence;

public sealed record StoredRunPayload(
    StoredSimulationMetadata Metadata,
    RecordingSettings Recording,
    TerminalRunSummary? Result)
{
    public static StoredRunPayload Create(SimulationMetadata metadata, RecordingSettings settings) =>
        new(StoredSimulationMetadata.FromMetadata(metadata), settings, null);
}

public sealed record StoredSimulationMetadata(
    Guid SessionId,
    SimulationRunIdentity RunIdentity,
    int GridWidth,
    int GridHeight,
    BoundaryMode BoundaryMode,
    CopyNeighbourhood CopyNeighbourhood,
    ContactCouplingNeighbourhood ContactCouplingNeighbourhood,
    PerimeterNeighbourhood PerimeterNeighbourhood,
    ConnectivityAdjacency ConnectivityAdjacency,
    int ExperimentSchemaVersion,
    CellTypeDefinition[] CellTypes,
    int[] CellTypeByCellId)
{
    public static StoredSimulationMetadata FromMetadata(SimulationMetadata metadata) =>
        new(
            metadata.SessionId,
            metadata.RunIdentity,
            metadata.GridWidth,
            metadata.GridHeight,
            metadata.BoundaryMode,
            metadata.CopyNeighbourhood,
            metadata.ContactCouplingNeighbourhood,
            metadata.PerimeterNeighbourhood,
            metadata.ConnectivityAdjacency,
            metadata.ExperimentSchemaVersion,
            metadata.CellTypes,
            metadata.CellTypeByCellId);

    public SimulationMetadata ToMetadata() => new(
        SessionId,
        RunIdentity,
        GridWidth,
        GridHeight,
        BoundaryMode,
        CopyNeighbourhood,
        ContactCouplingNeighbourhood,
        PerimeterNeighbourhood,
        ConnectivityAdjacency,
        ExperimentSchemaVersion,
        CellTypes,
        CellTypeByCellId);
}

public sealed record RecordingSettings(
    bool Enabled,
    int RecordEveryMcs,
    int KeyframeEveryRecordedFrames,
    double DeltaPromotionRatio,
    int EnvelopeVersion,
    int KeyframeCodecVersion,
    int DeltaCodecVersion,
    string Compression)
{
    public static RecordingSettings FromHeader(RecordingHeader header) => new(
        true,
        header.RecordEveryMcs,
        header.KeyframeEveryRecordedFrames,
        header.DeltaPromotionRatio,
        header.EnvelopeVersion,
        header.KeyframeCodecVersion,
        header.DeltaCodecVersion,
        header.Compression);

    public static RecordingSettings Disabled { get; } = new(
        false,
        5,
        20,
        0.75,
        RecordingFormat.EnvelopeVersion,
        RecordingFormat.KeyframeCodecVersion,
        RecordingFormat.DeltaCodecVersion,
        RecordingFormat.Compression);

    public RecordingHeader ToHeader(
        Guid sessionId,
        SimulationRunIdentity identity,
        string manifestJson,
        DateTimeOffset createdAtUtc,
        int width,
        int height,
        SimulationMetadata metadata) =>
        new(
            RecordingFormat.SchemaVersion,
            sessionId,
            identity,
            manifestJson,
            createdAtUtc,
            width,
            height,
            RecordEveryMcs,
            KeyframeEveryRecordedFrames,
            DeltaPromotionRatio,
            EnvelopeVersion,
            KeyframeCodecVersion,
            DeltaCodecVersion,
            Compression,
            metadata);
}

public sealed record TerminalRunSummary(
    SimulationSessionStatus Status,
    long FinalMcs,
    SimulationRunIdentity RunIdentity,
    PersistedMetricSample? FinalMeasurement,
    string? Failure,
    RecordingState RecordingState,
    string? RecordingFailure);

public sealed record PersistedRun(
    Guid Id,
    string ExperimentId,
    string ReplicateId,
    int ReplicateIndex,
    SimulationSessionStatus Status,
    long Revision,
    int Width,
    int Height,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    long CurrentMcs,
    string KernelId,
    string ManifestJson,
    string? ResultJson,
    string? Failure,
    RecordingState RecordingState,
    string? RecordingFailure,
    long RecordingBytes,
    StoredRunPayload Payload);

public sealed record PersistedMetricSample(long Mcs, IReadOnlyDictionary<string, double> Values);
