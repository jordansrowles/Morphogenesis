using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Random;

namespace Rowles.Morphogenesis.Dynamics.Acceleration;

public enum ProposalSpaceKind
{
    BorderSites,
    DirectedInterface
}

/// <summary>
/// Opt-in proposal-space kernel with an integer canonical-equivalent attempt clock.
/// Its geometric sampler approximates the continuous inverse CDF on the PRNG's finite grid;
/// trajectory identity with uniform-lattice sampling is neither expected nor claimed.
/// </summary>
public sealed class EventClockSimulation
{
    public const string ClockVersion = "geometric-canonical-slots-v1";

    private readonly IRandomSource _random;
    private readonly ProposalRandom _proposalRandom;
    private readonly SerialSimulation _evaluator;
    private readonly InterfaceIndex _index;
    private readonly ProposalSpaceKind _kind;
    private bool _waiting;
    private long _skipsRemaining;

    public EventClockSimulation(MorphogenesisState state, ulong seed, double fluctuationAmplitude, ProposalSpaceKind kind)
        : this(state, new Xoshiro256StarStar(seed), fluctuationAmplitude, kind)
    {
    }

    public EventClockSimulation(MorphogenesisState state, IRandomSource random, double fluctuationAmplitude, ProposalSpaceKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _kind = kind;
        _index = new InterfaceIndex(state);
        _proposalRandom = new ProposalRandom(random, state.SiteCount, _index.Degree);
        _evaluator = new SerialSimulation(state, _proposalRandom, fluctuationAmplitude);
    }

    public ProposalSpaceKind ProposalSpace => _kind;
    public string KernelId => _kind == ProposalSpaceKind.BorderSites ? "border-geometric-v1" : "directed-interface-geometric-v1";
    public MorphogenesisState State => _evaluator.State;
    public long CanonicalEquivalentAttempts { get; private set; }
    public long RawEventProposals => _evaluator.AttemptCount;
    public long SkippedNoOpAttempts => CanonicalEquivalentAttempts - RawEventProposals;
    public long RetainedNoOpEvents => NoOpProposals - SkippedNoOpAttempts;
    public long RejectedProposals => HardConstraintRejections + EnergyRejections + FixedWallRejections;
    public long FailedEventProposals => RawEventProposals - AcceptedCopies - RetainedNoOpEvents - RejectedProposals;
    public long CompletedMcs => CanonicalEquivalentAttempts / State.SiteCount;
    public long AcceptedCopies { get; private set; }
    public long NoOpProposals { get; private set; }
    public long HardConstraintRejections { get; private set; }
    public long EnergyRejections { get; private set; }
    public long FixedWallRejections { get; private set; }
    public long ConnectivityFallbacks => _evaluator.ConnectivityFallbackCount;
    public int BorderSiteCount => _index.BorderSites.Count;
    /// <summary>Labelled unlike-ID and fixed-wall proposals; wall directions are retained rejection events.</summary>
    public int DirectedInterfaceCount => _index.DirectedProposals.Count;

    public McsSummary RunMcs() => Advance(State.SiteCount);

    /// <summary>Advance exactly attempts slots; checkpoint partitioning never resamples pending skips.</summary>
    public McsSummary Advance(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attempts);
        _ = checked(CanonicalEquivalentAttempts + attempts);
        int remaining = attempts;
        int accepted = 0, rejected = 0, noOps = 0;
        long fallbacksBefore = ConnectivityFallbacks;
        while (remaining > 0)
        {
            int members = _kind == ProposalSpaceKind.BorderSites ? BorderSiteCount : DirectedInterfaceCount;
            if (members == 0)
            {
                noOps += remaining;
                NoOpProposals += remaining;
                CanonicalEquivalentAttempts += remaining;
                break;
            }
            if (!_waiting)
            {
                long proposalCount = _kind == ProposalSpaceKind.BorderSites
                    ? State.SiteCount : (long)State.SiteCount * _index.Degree;
                _skipsRemaining = GeometricWaitingTime.Sample(members, proposalCount, _random);
                _waiting = true;
            }
            int skips = (int)Math.Min(_skipsRemaining, remaining);
            _skipsRemaining -= skips;
            remaining -= skips;
            noOps += skips;
            NoOpProposals += skips;
            CanonicalEquivalentAttempts += skips;
            if (remaining == 0) break;

            int target, direction;
            if (_kind == ProposalSpaceKind.BorderSites)
            {
                target = _index.BorderSites.Select(_random);
                direction = _random.NextInt(_index.Degree);
                if ((uint)direction >= (uint)_index.Degree)
                    throw new InvalidOperationException("The random source returned an invalid direction.");
            }
            else
            {
                int edge = _index.DirectedProposals.Select(_random);
                target = edge / _index.Degree;
                direction = edge % _index.Degree;
            }
            _proposalRandom.Prepare(target, direction);
            _waiting = false;
            remaining--;
            CanonicalEquivalentAttempts++;
            AttemptResult result = _evaluator.Attempt();
            switch (result.Status)
            {
                case AttemptStatus.Accepted:
                    accepted++;
                    AcceptedCopies++;
                    _index.UpdateAfterAcceptedCopy(target);
                    break;
                case AttemptStatus.NoOp:
                    noOps++;
                    NoOpProposals++;
                    break;
                case AttemptStatus.Rejected:
                    rejected++;
                    if (result.RejectionReason == RejectionReason.Metropolis) EnergyRejections++;
                    else if (result.RejectionReason == RejectionReason.FixedWall) FixedWallRejections++;
                    else HardConstraintRejections++;
                    break;
            }
        }
        return new McsSummary(attempts, accepted, rejected, noOps, checked((int)(ConnectivityFallbacks - fallbacksBefore)));
    }

    private sealed class ProposalRandom(IRandomSource acceptanceRandom, int siteCount, int degree) : IRandomSource
    {
        private int _target;
        private int _direction;
        private int _draw;

        internal void Prepare(int target, int direction)
        {
            _target = target;
            _direction = direction;
            _draw = 0;
        }

        public int NextInt(int exclusiveUpperBound)
        {
            if (_draw == 0 && exclusiveUpperBound == siteCount)
            {
                _draw++;
                return _target;
            }
            if (_draw == 1 && exclusiveUpperBound == degree)
            {
                _draw++;
                return _direction;
            }
            throw new InvalidOperationException("The event evaluator changed its proposal draw contract.");
        }

        public double NextDouble()
        {
            if (_draw != 2) throw new InvalidOperationException("Acceptance preceded proposal resolution.");
            return acceptanceRandom.NextDouble();
        }
    }
}
