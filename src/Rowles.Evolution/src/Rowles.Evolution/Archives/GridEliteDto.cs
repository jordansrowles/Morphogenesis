namespace Rowles.Evolution.Archives;

/// <summary>Versioned-checkpoint representation of one archive entry.</summary>
public sealed record GridEliteDto(int CellIndex, long CandidateId, double[] Solution, double Objective, double[] Descriptors, long Iteration);
