namespace Rowles.Morphogenesis.Server.Sessions;

public sealed record SessionCommandRequest(Guid CommandId, long ExpectedRevision);
