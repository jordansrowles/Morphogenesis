using System.Globalization;
using Rowles.Morphogenesis.Diagnostics;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Dynamics.Acceleration;
using Rowles.Morphogenesis.Initialisation;

namespace Rowles.Morphogenesis.Benchmarks;

internal static class ProposalSpaceProfile
{
    internal static void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using StreamWriter writer = new(path);
        writer.WriteLine("scenario,kernel,clock_version,mcs,canonical_attempts,raw_events,skipped_no_ops,retained_no_ops,accepted,hard_rejected,energy_rejected,wall_rejected,fallbacks,initial_borders,initial_directed,final_borders,final_directed");
        foreach (M3BenchmarkScenario scenario in M3BenchmarkScenario.All)
        {
            foreach (string kernel in new[] { "canonical", "border", "directed" })
            {
                var manifest = scenario.Manifest;
                var state = PackedAggregateInitialiser.Create(manifest, manifest.BaseSeed).State;
                InterfaceIndex initial = new(state);
                int initialBorders = initial.BorderSites.Count;
                int initialDirected = initial.DirectedProposals.Count;
                long accepted = 0, noOps = 0, hard = 0, energy = 0, walls = 0, raw = 0, skipped = 0, fallbacks;
                if (kernel == "canonical")
                {
                    SerialSimulation simulation = new(state, manifest.BaseSeed, manifest.FluctuationAmplitude);
                    for (int mcs = 0; mcs < scenario.KernelMcsPerInvocation; mcs++)
                    {
                        for (int slot = 0; slot < state.SiteCount; slot++)
                        {
                            AttemptResult result = simulation.Attempt();
                            if (result.Status == AttemptStatus.Accepted) accepted++;
                            else if (result.Status == AttemptStatus.NoOp) noOps++;
                            else if (result.RejectionReason == RejectionReason.Metropolis) energy++;
                            else if (result.RejectionReason == RejectionReason.FixedWall) walls++;
                            else hard++;
                        }
                    }
                    raw = simulation.AttemptCount;
                    fallbacks = simulation.ConnectivityFallbackCount;
                }
                else
                {
                    EventClockSimulation simulation = new(state, manifest.BaseSeed, manifest.FluctuationAmplitude,
                        kernel == "border" ? ProposalSpaceKind.BorderSites : ProposalSpaceKind.DirectedInterface);
                    for (int mcs = 0; mcs < scenario.KernelMcsPerInvocation; mcs++) simulation.RunMcs();
                    accepted = simulation.AcceptedCopies;
                    noOps = simulation.NoOpProposals;
                    hard = simulation.HardConstraintRejections;
                    energy = simulation.EnergyRejections;
                    walls = simulation.FixedWallRejections;
                    raw = simulation.RawEventProposals;
                    skipped = simulation.SkippedNoOpAttempts;
                    fallbacks = simulation.ConnectivityFallbacks;
                    if (simulation.FailedEventProposals != 0) throw new InvalidOperationException("An event failed during profiling.");
                }
                long attempts = (long)state.SiteCount * scenario.KernelMcsPerInvocation;
                if (accepted + noOps + hard + energy + walls != attempts || raw + skipped != attempts)
                    throw new InvalidOperationException("Proposal counters do not reconcile with the canonical-equivalent clock.");
                InvariantValidator.Validate(state);
                InterfaceIndex final = new(state);
                writer.WriteLine(string.Join(',', new object[]
                {
                    scenario.Id, kernel, kernel == "canonical" ? "uniform-lattice-v1" : EventClockSimulation.ClockVersion,
                    scenario.KernelMcsPerInvocation, attempts, raw, skipped, noOps - skipped,
                    accepted, hard, energy, walls, fallbacks, initialBorders, initialDirected,
                    final.BorderSites.Count, final.DirectedProposals.Count
                }.Select(value => value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString())));
            }
        }
    }
}
