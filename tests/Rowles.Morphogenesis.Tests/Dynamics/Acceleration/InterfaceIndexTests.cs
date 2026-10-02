using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Dynamics.Acceleration;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Initialisation;
using Rowles.Morphogenesis.Lattice;

namespace Rowles.Morphogenesis.Tests.Dynamics.Acceleration;

public sealed class InterfaceIndexTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Incremental_border_and_directed_membership_match_full_recomputation(bool wall)
    {
        ExperimentManifest manifest = ExperimentManifest.ReadJson(Path.Combine(
            AppContext.BaseDirectory, "experiments", "canonical", "E02-sorting.json"));
        manifest = manifest with { BoundaryMode = wall ? BoundaryMode.Wall : BoundaryMode.Periodic };
        foreach (ulong seed in new ulong[] { 17, 123, 20260929 })
        {
            var state = PackedAggregateInitialiser.Create(manifest, seed).State;
            SerialSimulation simulation = new(state, seed, manifest.FluctuationAmplitude);
            InterfaceIndex index = new(state);
            Check();
            for (int attempt = 0; attempt < 10000; attempt++)
            {
                AttemptResult result = simulation.Attempt();
                if (result.Status == AttemptStatus.Accepted) index.UpdateAfterAcceptedCopy(result.TargetIndex);
                if (result.Status == AttemptStatus.Accepted || attempt % 100 == 0) Check();
            }

            void Check()
            {
                int borders = 0, edges = 0;
                for (int target = 0; target < state.SiteCount; target++)
                {
                    bool border = false;
                    for (int direction = 0; direction < index.Degree; direction++)
                    {
                        // Independent four-neighbour reference, without InterfaceIndex.Resolve.
                        int dx = direction == 3 ? -1 : direction == 1 ? 1 : 0;
                        int dy = direction == 0 ? -1 : direction == 2 ? 1 : 0;
                        int x = target % state.Width + dx, y = target / state.Width + dy;
                        bool outside = x < 0 || y < 0 || x >= state.Width || y >= state.Height;
                        int source = outside && wall ? -1 :
                            ((y + state.Height) % state.Height) * state.Width + (x + state.Width) % state.Width;
                        bool eligible = source < 0 || state.Lattice[target] != state.Lattice[source];
                        Assert.Equal(eligible, index.DirectedProposals.Contains(target * index.Degree + direction));
                        if (eligible) { edges++; border = true; }
                    }
                    Assert.Equal(border, index.BorderSites.Contains(target));
                    if (border) borders++;
                }
                Assert.Equal(borders, index.BorderSites.Count);
                Assert.Equal(edges, index.DirectedProposals.Count);
            }
        }
    }
}
