namespace Rowles.Evolution.Archives;

/// <summary>Dense deterministic Grid MAP-Elites archive. Occupied elites are enumerated by cell index.</summary>
public sealed class GridArchive
{
    private readonly GridElite?[] cells;

    public GridArchive(GridArchiveConfiguration configuration)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        cells = new GridElite[configuration.CellCount];
    }

    public GridArchiveConfiguration Configuration { get; }
    public int Occupancy { get; private set; }
    public double Coverage => (double)Occupancy / cells.Length;
    public IEnumerable<GridElite> OccupiedElites
    {
        get
        {
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] is { } elite) yield return elite;
        }
    }

    public GridElite? GetAt(int cellIndex)
    {
        if ((uint)cellIndex >= (uint)cells.Length) throw new ArgumentOutOfRangeException(nameof(cellIndex));
        return cells[cellIndex];
    }

    public bool TryInsert(long candidateId, ReadOnlySpan<double> solution, double objective,
        IReadOnlyList<double> descriptors, long iteration, out bool replaced)
    {
        if (!double.IsFinite(objective)) throw new ArgumentOutOfRangeException(nameof(objective));
        if (iteration < 0) throw new ArgumentOutOfRangeException(nameof(iteration));
        int index = Configuration.GetCellIndex(descriptors);
        GridElite? incumbent = cells[index];
        bool improves = incumbent is null || (Configuration.ObjectiveDirection == ObjectiveDirection.Maximise
            ? objective > incumbent.Objective
            : objective < incumbent.Objective);
        replaced = improves && incumbent is not null;
        if (!improves) return false;
        cells[index] = new GridElite(index, candidateId, solution, objective, descriptors.ToArray(), iteration);
        if (incumbent is null) Occupancy++;
        return true;
    }

    internal GridEliteDto[] ToDtos() => OccupiedElites.Select(e => new GridEliteDto(e.CellIndex, e.CandidateId,
        e.CopySolution(), e.Objective, e.CopyDescriptors(), e.Iteration)).ToArray();

    internal void Load(GridEliteDto[] elites)
    {
        ArgumentNullException.ThrowIfNull(elites);
        HashSet<long> candidateIds = [];
        foreach (GridEliteDto elite in elites)
        {
            if (elite is null || (uint)elite.CellIndex >= (uint)cells.Length || cells[elite.CellIndex] is not null ||
                elite.CandidateId < 0 || !candidateIds.Add(elite.CandidateId) || elite.Iteration < 0 ||
                !double.IsFinite(elite.Objective) || elite.Descriptors is null || elite.Solution is null)
                throw new ArgumentException("Checkpoint contains an invalid or duplicate archive elite.", nameof(elites));

            if (Configuration.GetCellIndex(elite.Descriptors) != elite.CellIndex)
                throw new ArgumentException("Checkpoint elite descriptors do not map to the stored cell index.", nameof(elites));

            cells[elite.CellIndex] = new GridElite(elite.CellIndex, elite.CandidateId, elite.Solution,
                elite.Objective, elite.Descriptors, elite.Iteration);
            Occupancy++;
        }
    }
}
