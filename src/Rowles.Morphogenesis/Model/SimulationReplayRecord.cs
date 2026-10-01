using Rowles.Morphogenesis.Lattice;

namespace Rowles.Morphogenesis.Model;

public readonly record struct CellReplayParameters(
    int CellId,
    int CellTypeId,
    double TargetArea,
    double AreaStiffness,
    double TargetPerimeter,
    double PerimeterStiffness);

public sealed class SimulationReplayRecord
{
    private readonly CellReplayParameters[] _cells;
    private readonly double[,] _contactEnergies;
    private readonly int[] _cellIds;

    internal SimulationReplayRecord(
        int width,
        int height,
        int[] initialCellIds,
        IReadOnlyList<CellReplayParameters> cells,
        double[,] contactEnergies,
        BoundaryMode boundaryMode,
        LatticeConventions conventions,
        ulong seed,
        string randomAlgorithm,
        double fluctuationAmplitude,
        long attemptCount,
        long completedMcs)
    {
        ArgumentNullException.ThrowIfNull(initialCellIds);
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(contactEnergies);
        ArgumentException.ThrowIfNullOrWhiteSpace(randomAlgorithm);

        Width = width;
        Height = height;
        BoundaryMode = boundaryMode;
        Conventions = conventions;
        Seed = seed;
        RandomAlgorithm = randomAlgorithm;
        KernelVersion = SerialKernelVersion;
        ReplayFormatVersion = CurrentReplayFormatVersion;
        FluctuationAmplitude = fluctuationAmplitude;
        AttemptCount = attemptCount;
        CompletedMcs = completedMcs;
        _cellIds = (int[])initialCellIds.Clone();
        _cells = cells.ToArray();
        _contactEnergies = (double[,])contactEnergies.Clone();
    }

    public const string SerialKernelVersion = "serial-cpm-v1";

    public const string CurrentReplayFormatVersion = "morphogenesis-replay-v1";

    public int Width { get; }

    public int Height { get; }

    public BoundaryMode BoundaryMode { get; }

    public LatticeConventions Conventions { get; }

    public ulong Seed { get; }

    public string RandomAlgorithm { get; }

    public string KernelVersion { get; }

    public string ReplayFormatVersion { get; }

    public double FluctuationAmplitude { get; }

    /// <summary>
    /// Number of serial <c>Attempt()</c> calls already executed by the recorded run.
    /// This is the authoritative replay length.
    /// </summary>
    public long AttemptCount { get; }

    /// <summary>
    /// Number of completed <c>RunMcs()</c> calls in the recorded run.
    /// This is progress metadata only; run replay is defined by <see cref="AttemptCount"/>.
    /// </summary>
    public long CompletedMcs { get; }

    public CellReplayParameters[] Cells => (CellReplayParameters[])_cells.Clone();

    public double[,] ContactEnergies => (double[,])_contactEnergies.Clone();

    /// <summary>
    /// Initial lattice cell IDs for the run, not the lattice state at record creation time.
    /// </summary>
    public int[] CellIds => (int[])_cellIds.Clone();
}
