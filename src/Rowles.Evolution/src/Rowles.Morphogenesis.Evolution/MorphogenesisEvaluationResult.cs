using Rowles.Evolution.Evaluation;

namespace Rowles.Morphogenesis.Optimisation;

public sealed record MorphogenesisEvaluationResult(CandidateEvaluation Evaluation,
    double ElapsedMilliseconds, long AllocatedBytes, double[] ReplicateElapsedMilliseconds);
