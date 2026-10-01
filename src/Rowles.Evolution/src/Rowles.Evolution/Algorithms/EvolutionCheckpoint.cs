using Rowles.Evolution.Archives;
using Rowles.Evolution.Random;

namespace Rowles.Evolution.Algorithms;

/// <summary>Explicit checkpoint schema; runtime algorithm objects are never serialized.</summary>
public sealed record EvolutionCheckpoint(int SchemaVersion, NumericBoundsDto[] SolutionBounds,
    double[] DescriptorLowerBounds, double[] DescriptorUpperBounds, int[] BinCounts,
    ObjectiveDirection ObjectiveDirection, double? ObjectiveBaseline, int MaximumCells,
    double SigmaIso, double SigmaLine, RandomState RandomState, long NextCandidateId, long Iteration,
    long Evaluations, long Insertions, long Replacements, long InvalidEvaluations,
    FailureCountDto[] FailureCounts, GridEliteDto[] Elites);
