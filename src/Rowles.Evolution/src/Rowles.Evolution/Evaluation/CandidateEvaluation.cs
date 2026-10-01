namespace Rowles.Evolution.Evaluation;

/// <summary>The complete result of evaluating one candidate.</summary>
public sealed record CandidateEvaluation
{
    private CandidateEvaluation(long candidateId, bool isValid, double objective, double[] descriptors, string? failureReason)
    {
        CandidateId = candidateId;
        IsValid = isValid;
        Objective = objective;
        Descriptors = Array.AsReadOnly(descriptors);
        FailureReason = failureReason;
    }

    public long CandidateId { get; }
    public bool IsValid { get; }
    public double Objective { get; }
    public IReadOnlyList<double> Descriptors { get; }
    public string? FailureReason { get; }

    public static CandidateEvaluation Valid(long candidateId, double objective, IEnumerable<double> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        return new CandidateEvaluation(candidateId, true, objective, descriptors.ToArray(), null);
    }

    public static CandidateEvaluation Invalid(long candidateId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new CandidateEvaluation(candidateId, false, double.NaN, [], reason);
    }
}
