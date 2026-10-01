namespace Rowles.Morphogenesis.Experiments.Results;

public sealed record ExperimentRunMetadata(
    string SoftwareVersion,
    string? SoftwareCommit,
    string RuntimeVersion,
    string? SdkVersion,
    string OperatingSystem,
    string Architecture,
    string KernelId,
    string SourceTreeState);
