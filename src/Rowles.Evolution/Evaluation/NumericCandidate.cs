namespace Rowles.Evolution.Evaluation;

/// <summary>A bounded numeric solution with a stable identity and ask iteration.</summary>
public sealed class NumericCandidate
{
    private readonly double[] values;

    public NumericCandidate(long candidateId, ReadOnlySpan<double> values, long iteration)
    {
        if (candidateId < 0) throw new ArgumentOutOfRangeException(nameof(candidateId));
        if (iteration < 0) throw new ArgumentOutOfRangeException(nameof(iteration));
        CandidateId = candidateId;
        Iteration = iteration;
        this.values = values.ToArray();
        Values = Array.AsReadOnly(this.values);
    }

    public long CandidateId { get; }
    public long Iteration { get; }
    public IReadOnlyList<double> Values { get; }
    internal double[] CopyValues() => values.ToArray();
}
