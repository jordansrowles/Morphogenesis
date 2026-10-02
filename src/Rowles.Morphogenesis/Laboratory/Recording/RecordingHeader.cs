using Rowles.Morphogenesis.Laboratory.Sessions;

namespace Rowles.Morphogenesis.Laboratory.Recording;

public sealed record RecordingHeader(
    int RecordingSchemaVersion,
    Guid SessionId,
    SimulationRunIdentity SimulationRunIdentity,
    string ExperimentManifestJson,
    DateTimeOffset CreatedAtUtc,
    int Width,
    int Height,
    int RecordEveryMcs,
    int KeyframeEveryRecordedFrames,
    double DeltaPromotionRatio,
    int EnvelopeVersion,
    int KeyframeCodecVersion,
    int DeltaCodecVersion,
    string Compression,
    SimulationMetadata Metadata);
