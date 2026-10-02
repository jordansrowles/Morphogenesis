// Opt-in diagnostic copy. Differential tests require exact parity with the canonical kernel.
using System.Diagnostics;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Random;
using Rowles.Morphogenesis.Topology;
using Rowles.StrictMaths;

namespace Rowles.Morphogenesis.Benchmarks.Diagnostics;

public sealed class ProfiledSerialSimulation
{
    private readonly IRandomSource _random;
    private readonly ConnectivityWorkspace _connectivityWorkspace;
    private readonly int[] _initialCellIds;
    private readonly CellReplayParameters[] _initialCells;
    private readonly double[,] _initialContactEnergies;
    private readonly BoundaryMode _initialBoundaryMode;
    private readonly LatticeConventions _initialConventions;

    public ProfiledSerialSimulation(MorphogenesisState state, IRandomSource random, double fluctuationAmplitude)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        if (!double.IsFinite(fluctuationAmplitude) || fluctuationAmplitude < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fluctuationAmplitude));
        }

        FluctuationAmplitude = fluctuationAmplitude;
        _connectivityWorkspace = new ConnectivityWorkspace(state.SiteCount);
        RandomAlgorithm = random is Xoshiro256StarStar
            ? Xoshiro256StarStar.AlgorithmName
            : random.GetType().FullName ?? random.GetType().Name;

        _initialCellIds = state.GetCellIdsCopy();
        _initialContactEnergies = state.ContactEnergies.ToArray();
        _initialBoundaryMode = state.Configuration.BoundaryMode;
        _initialConventions = state.Configuration.Conventions;
        List<CellReplayParameters> initialCells = [];
        for (int cellId = 1; cellId < state.Cells.Length; cellId++)
        {
            CellRuntime cell = state.Cells[cellId];
            if (cell.IsAlive)
            {
                initialCells.Add(new CellReplayParameters(
                    cellId,
                    cell.CellTypeId,
                    cell.TargetArea,
                    cell.AreaStiffness,
                    cell.TargetPerimeter,
                    cell.PerimeterStiffness));
            }
        }

        _initialCells = initialCells.ToArray();
    }

    public ProfiledSerialSimulation(MorphogenesisState state, ulong seed, double fluctuationAmplitude)
        : this(state, new Xoshiro256StarStar(seed), fluctuationAmplitude)
    {
        Seed = seed;
    }

    public MorphogenesisState State { get; }

    public double FluctuationAmplitude { get; }

    public long CompletedMcs { get; private set; }

    public long ConnectivityFallbackCount { get; private set; }

    public long AttemptCount { get; private set; }

    public ulong? Seed { get; }

    public string RandomAlgorithm { get; }

    public SimulationReplayRecord CreateReplayRecord()
    {
        if (Seed is null || RandomAlgorithm != Xoshiro256StarStar.AlgorithmName)
        {
            throw new InvalidOperationException(
                "Deterministic run replay is supported only for simulations created with the seeded xoshiro256** constructor.");
        }

        return new SimulationReplayRecord(
            State.Width,
            State.Height,
            _initialCellIds,
            _initialCells,
            _initialContactEnergies,
            _initialBoundaryMode,
            _initialConventions,
            Seed.Value,
            RandomAlgorithm,
            FluctuationAmplitude,
            AttemptCount,
            CompletedMcs);
    }

    public static ProfiledSerialSimulation FromReplayRecord(SimulationReplayRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.ReplayFormatVersion != SimulationReplayRecord.CurrentReplayFormatVersion)
        {
            throw new NotSupportedException($"Unsupported replay format '{record.ReplayFormatVersion}'.");
        }

        if (record.KernelVersion != SimulationReplayRecord.SerialKernelVersion)
        {
            throw new NotSupportedException($"Unsupported serial kernel '{record.KernelVersion}'.");
        }

        if (record.RandomAlgorithm != Xoshiro256StarStar.AlgorithmName)
        {
            throw new NotSupportedException($"Unsupported replay random algorithm '{record.RandomAlgorithm}'.");
        }

        CellDefinition[] cells = record.Cells
            .Select(cell => new CellDefinition(
                cell.CellId,
                cell.CellTypeId,
                cell.TargetArea,
                cell.AreaStiffness,
                cell.TargetPerimeter,
                cell.PerimeterStiffness))
            .ToArray();

        MorphogenesisState state = new(
            record.Width,
            record.Height,
            record.CellIds,
            cells,
            new ContactEnergyMatrix(record.ContactEnergies),
            new SimulationConfiguration(record.BoundaryMode, record.Conventions));

        return new ProfiledSerialSimulation(state, record.Seed, record.FluctuationAmplitude);
    }

    public ProposalStageProfile Profile { get; } = new();

    public AttemptResult Attempt()
    {
        long stageStart = Stopwatch.GetTimestamp();
        AttemptCount = checked(AttemptCount + 1);
        int targetIndex = _random.NextInt(State.SiteCount);
        if ((uint)targetIndex >= (uint)State.SiteCount)
        {
            throw new InvalidOperationException("The random source returned a target index outside its requested range.");
        }

        int direction = _random.NextInt(StencilGeometry.Count(State.Configuration.Conventions.CopyNeighbourhood));
        if ((uint)direction >= (uint)StencilGeometry.Count(State.Configuration.Conventions.CopyNeighbourhood))
        {
            throw new InvalidOperationException("The random source returned a copy-neighbour index outside its requested range.");
        }

        Profile.Record(ProposalStage.ProposalRng, stageStart);
        stageStart = Stopwatch.GetTimestamp();
        StencilGeometry.CopyOffset(State.Configuration.Conventions.CopyNeighbourhood, direction, out int dx, out int dy);
        int sourceIndex = State.Resolve(targetIndex, dx, dy);
        Profile.Record(ProposalStage.BoundaryResolve, stageStart);
        stageStart = Stopwatch.GetTimestamp();
        int oldCellId = State.Lattice[targetIndex];
        if (sourceIndex < 0)
        {
            return Rejected(targetIndex, -1, oldCellId, 0, RejectionReason.FixedWall);
        }

        int newCellId = State.Lattice[sourceIndex];
        Profile.Record(ProposalStage.SameIdCheck, stageStart);
        if (oldCellId == newCellId)
        {
            return new AttemptResult(
                targetIndex,
                sourceIndex,
                oldCellId,
                newCellId,
                AttemptStatus.NoOp,
                RejectionReason.None,
                default,
                null,
                null,
                false);
        }

        bool usedFallback = false;
        if (oldCellId > 0)
        {
            stageStart = Stopwatch.GetTimestamp();
            CellRuntime losingCell = State.Cells[oldCellId];
            if (losingCell.Area <= 1)
            {
                Profile.Record(ProposalStage.FinalSiteConstraint, stageStart);
                return Rejected(targetIndex, sourceIndex, oldCellId, newCellId, RejectionReason.FinalSite);
            }

            Profile.Record(ProposalStage.FinalSiteConstraint, stageStart);
            ConnectivityEvaluation connectivity = ProfiledCellConnectivity.EvaluateRemoval(State, targetIndex, _connectivityWorkspace, Profile);
            usedFallback = connectivity.UsedGlobalFallback;
            if (usedFallback)
            {
                ConnectivityFallbackCount++;
            }

            if (!connectivity.RemainsConnected)
            {
                return Rejected(targetIndex, sourceIndex, oldCellId, newCellId, RejectionReason.Disconnected, usedFallback);
            }
        }

        MoveEvaluation evaluation = ProfiledEnergyDeltaCalculator.Evaluate(State, targetIndex, newCellId, Profile);
        stageStart = Stopwatch.GetTimestamp();
        double probability = AcceptanceProbability(evaluation.Total, FluctuationAmplitude);
        if (evaluation.Total <= 0)
        {
            Profile.Record(ProposalStage.Acceptance, stageStart);
            TimedCommit(targetIndex, oldCellId, newCellId, evaluation);
            return Accepted(targetIndex, sourceIndex, oldCellId, newCellId, evaluation, probability, null, usedFallback);
        }

        double randomValue = _random.NextDouble();
        if (!double.IsFinite(randomValue) || randomValue < 0 || randomValue >= 1)
        {
            throw new InvalidOperationException("The random source must return acceptance values in [0, 1).");
        }

        bool accepted = ShouldAccept(probability, randomValue);
        Profile.Record(ProposalStage.Acceptance, stageStart);
        if (accepted)
        {
            TimedCommit(targetIndex, oldCellId, newCellId, evaluation);
            return Accepted(targetIndex, sourceIndex, oldCellId, newCellId, evaluation, probability, randomValue, usedFallback);
        }

        return new AttemptResult(
            targetIndex,
            sourceIndex,
            oldCellId,
            newCellId,
            AttemptStatus.Rejected,
            RejectionReason.Metropolis,
            evaluation.Terms,
            probability,
            randomValue,
            usedFallback);
    }

    public McsSummary RunMcs()
    {
        int accepted = 0;
        int rejected = 0;
        int noOps = 0;
        long fallbacksBefore = ConnectivityFallbackCount;
        for (int index = 0; index < State.SiteCount; index++)
        {
            AttemptResult result = Attempt();
            switch (result.Status)
            {
                case AttemptStatus.Accepted:
                    accepted++;
                    break;
                case AttemptStatus.Rejected:
                    rejected++;
                    break;
                case AttemptStatus.NoOp:
                    noOps++;
                    break;
            }
        }

        CompletedMcs++;
        int fallbacks = checked((int)(ConnectivityFallbackCount - fallbacksBefore));
        return new McsSummary(State.SiteCount, accepted, rejected, noOps, fallbacks);
    }

    public static double AcceptanceProbability(double deltaH, double fluctuationAmplitude)
    {
        if (double.IsNaN(deltaH))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaH));
        }

        if (!double.IsFinite(fluctuationAmplitude) || fluctuationAmplitude < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fluctuationAmplitude));
        }

        if (deltaH <= 0)
        {
            return 1;
        }

        if (fluctuationAmplitude == 0)
        {
            return 0;
        }

        return StrictMath.Exp(-deltaH / fluctuationAmplitude);
    }

    public static bool ShouldAccept(double probability, double acceptanceRandomValue)
    {
        if (!double.IsFinite(probability) || probability < 0 || probability > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(probability));
        }

        if (!double.IsFinite(acceptanceRandomValue) || acceptanceRandomValue < 0 || acceptanceRandomValue >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(acceptanceRandomValue));
        }

        return acceptanceRandomValue < probability;
    }

    private static AttemptResult Rejected(
        int targetIndex,
        int sourceIndex,
        int oldCellId,
        int newCellId,
        RejectionReason reason,
        bool usedFallback = false) =>
        new(
            targetIndex,
            sourceIndex,
            oldCellId,
            newCellId,
            AttemptStatus.Rejected,
            reason,
            default,
            null,
            null,
            usedFallback);

    private static AttemptResult Accepted(
        int targetIndex,
        int sourceIndex,
        int oldCellId,
        int newCellId,
        MoveEvaluation evaluation,
        double probability,
        double? acceptanceRandomValue,
        bool usedFallback) =>
        new(
            targetIndex,
            sourceIndex,
            oldCellId,
            newCellId,
            AttemptStatus.Accepted,
            RejectionReason.None,
            evaluation.Terms,
            probability,
            acceptanceRandomValue,
            usedFallback);

    private void TimedCommit(int targetIndex, int oldCellId, int newCellId, MoveEvaluation evaluation)
    {
        long start = Stopwatch.GetTimestamp();
        Commit(targetIndex, oldCellId, newCellId, evaluation);
        Profile.Record(ProposalStage.AcceptedCommit, start);
    }

    private void Commit(int targetIndex, int oldCellId, int newCellId, MoveEvaluation evaluation)
    {
        State.Lattice[targetIndex] = newCellId;
        if (oldCellId > 0)
        {
            State.Cells[oldCellId].Area--;
            State.Cells[oldCellId].Perimeter += evaluation.OldCellPerimeterDelta;
        }

        if (newCellId > 0)
        {
            State.Cells[newCellId].Area++;
            State.Cells[newCellId].Perimeter += evaluation.NewCellPerimeterDelta;
        }
    }
}
