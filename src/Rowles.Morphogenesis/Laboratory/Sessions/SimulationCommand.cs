namespace Rowles.Morphogenesis.Laboratory.Sessions;

public sealed record SimulationCommand(
    Guid CommandId,
    long ExpectedRevision,
    SimulationCommandKind Kind,
    string? Failure = null);
