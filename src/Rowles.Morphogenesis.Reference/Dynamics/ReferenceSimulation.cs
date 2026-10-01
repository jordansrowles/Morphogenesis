using Rowles.Morphogenesis.Reference.Energy;
using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Model;
using Rowles.Morphogenesis.Reference.Random;
using Rowles.StrictMaths;

namespace Rowles.Morphogenesis.Reference.Dynamics;

public sealed class ReferenceSimulation
{
    private readonly IRandomSource _random;

    public ReferenceSimulation(ReferenceState state, IRandomSource random, double fluctuationAmplitude)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        if (!double.IsFinite(fluctuationAmplitude) || fluctuationAmplitude < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fluctuationAmplitude), "Fluctuation amplitude must be finite and non-negative.");
        }

        FluctuationAmplitude = fluctuationAmplitude;
    }

    public ReferenceState State { get; }

    public double FluctuationAmplitude { get; }

    public long CompletedMcs { get; private set; }

    public AttemptResult Attempt()
    {
        int targetIndex = _random.NextInt(State.SiteCount);
        if ((uint)targetIndex >= (uint)State.SiteCount)
        {
            throw new InvalidOperationException("The random source returned a target index outside its requested range.");
        }

        GridPoint target = new(targetIndex % State.Width, targetIndex / State.Width);
        ReadOnlySpan<GridPoint> copyOffsets = State.Conventions.CopyNeighbourhood.Offsets;
        int offsetIndex = _random.NextInt(copyOffsets.Length);
        if ((uint)offsetIndex >= (uint)copyOffsets.Length)
        {
            throw new InvalidOperationException("The random source returned a copy-neighbour index outside its requested range.");
        }

        GridPoint source = PeriodicLattice.Resolve(target, copyOffsets[offsetIndex], State.Width, State.Height);
        int oldId = State.CellIdAt(target);
        int newId = State.CellIdAt(source);
        if (oldId == newId)
        {
            return new AttemptResult(
                target,
                source,
                oldId,
                newId,
                AttemptStatus.NoOp,
                new HamiltonianBreakdown(0, 0, 0),
                null,
                null);
        }

        MoveDelta move = LocalMoveDelta.Evaluate(State, source, target);
        double probability = AcceptanceProbability(move.Total, FluctuationAmplitude);
        if (move.Total <= 0)
        {
            State.SetCellId(target, newId);
            return new AttemptResult(
                target,
                source,
                oldId,
                newId,
                AttemptStatus.Accepted,
                move.Terms,
                probability,
                null);
        }

        double randomValue = _random.NextDouble();
        if (!double.IsFinite(randomValue) || randomValue < 0 || randomValue >= 1)
        {
            throw new InvalidOperationException("The random source must return acceptance values in [0, 1).");
        }

        bool accepted = ShouldAccept(probability, randomValue);
        if (accepted)
        {
            State.SetCellId(target, newId);
        }

        return new AttemptResult(
            target,
            source,
            oldId,
            newId,
            accepted ? AttemptStatus.Accepted : AttemptStatus.Rejected,
            move.Terms,
            probability,
            randomValue);
    }

    public IReadOnlyList<AttemptResult> RunMcs()
    {
        AttemptResult[] attempts = new AttemptResult[State.SiteCount];
        for (int index = 0; index < attempts.Length; index++)
        {
            attempts[index] = Attempt();
        }

        CompletedMcs++;
        return attempts;
    }

    public static double AcceptanceProbability(double deltaH, double fluctuationAmplitude)
    {
        if (double.IsNaN(deltaH))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaH));
        }

        if (!double.IsFinite(fluctuationAmplitude) || fluctuationAmplitude < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fluctuationAmplitude), "Fluctuation amplitude must be finite and non-negative.");
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
}
