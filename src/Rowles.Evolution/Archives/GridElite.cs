namespace Rowles.Evolution.Archives;

/// <summary>An immutable elite occupying one flattened grid cell.</summary>
public sealed class GridElite
{
    private readonly double[] solution;
    private readonly double[] descriptors;

    internal GridElite(int cellIndex, long candidateId, ReadOnlySpan<double> solution, double objective,
        ReadOnlySpan<double> descriptors, long iteration)
    {
        CellIndex = cellIndex;
        CandidateId = candidateId;
        this.solution = solution.ToArray();
        this.descriptors = descriptors.ToArray();
        Solution = Array.AsReadOnly(this.solution);
        Descriptors = Array.AsReadOnly(this.descriptors);
        Objective = objective;
        Iteration = iteration;
    }

    public int CellIndex { get; }
    public long CandidateId { get; }
    public IReadOnlyList<double> Solution { get; }
    public double Objective { get; }
    public IReadOnlyList<double> Descriptors { get; }
    public long Iteration { get; }
    internal double[] CopySolution() => solution.ToArray();
    internal double[] CopyDescriptors() => descriptors.ToArray();
}
