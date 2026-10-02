namespace Rowles.Morphogenesis.Laboratory.Sessions;

public enum SimulationCommandDisposition
{
    Applied,
    Conflict,
    InvalidState,
    Terminal
}

public sealed record SimulationCommandResult(
    Guid SessionId,
    long Revision,
    SimulationSessionStatus Status,
    long CurrentMcs,
    SimulationCommandDisposition Disposition,
    long? ExpectedRevision = null);
