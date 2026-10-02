using System.Globalization;
using Rowles.Morphogenesis.Diagnostics;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Dynamics.Acceleration;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Random;
using Rowles.Morphogenesis.Initialisation;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Measurements;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Benchmarks;

/// <summary>Scientific capture; measurements and diagnostic accounting are deliberately outside headline benchmarks.</summary>
internal static class EventEnsembleCapture
{
    internal static void Run(string kernel, string directory, int seeds)
    {
        if (kernel is not ("canonical" or "border" or "directed")) throw new ArgumentException("Unknown kernel.", nameof(kernel));
        if (seeds is not (64 or 128 or 256)) throw new ArgumentOutOfRangeException(nameof(seeds));
        Directory.CreateDirectory(directory);
        if (kernel != "canonical" && !File.Exists(Path.Combine(directory, "canonical-bands-64.csv")))
            throw new InvalidOperationException("Freeze canonical bands before observing accelerated scientific metrics.");
        using StreamWriter samples = new(Path.Combine(directory, $"{kernel}-samples-{seeds}.csv"));
        using StreamWriter cells = new(Path.Combine(directory, $"{kernel}-cells-{seeds}.csv"));
        samples.WriteLine("condition,kernel,replicate,initialisation_seed,dynamics_seed,initial_hash,mcs,mixing,heterotypic,total_interface,homotypic_a,homotypic_b,domains_a,domains_b,largest_domain_a,largest_domain_b,area_mean,area_sd,area_q10,area_q50,area_q90,perimeter_mean,perimeter_sd,perimeter_q10,perimeter_q50,perimeter_q90,shape_mean,occupied_fraction,acceptance_fraction,canonical_attempts,raw_events,no_ops,hard_rejections,energy_rejections,wall_rejections,fallbacks,border_sites,directed_proposals");
        cells.WriteLine("condition,kernel,replicate,mcs,cell_id,type_id,area,perimeter,shape");
        foreach ((string condition, ExperimentManifest manifest) in Conditions(seeds))
        {
            File.WriteAllText(Path.Combine(directory, $"{condition}-manifest-{seeds}.json"), manifest.ToJson());
            for (int replicate = 0; replicate < seeds; replicate++)
            {
                ulong seed = ReplicateSeedDerivation.Derive(manifest.BaseSeed, replicate);
                ulong initialisationSeed = ExperimentSeedDerivation.DeriveInitialisation(seed);
                ulong dynamicsSeed = ExperimentSeedDerivation.DeriveDynamics(seed);
                MorphogenesisState state = PackedAggregateInitialiser.Create(manifest, initialisationSeed).State;
                ulong initialHash = Hash(state.GetCellIdsCopy());
                SerialSimulation? canonical = kernel == "canonical" ? new(state, dynamicsSeed, manifest.FluctuationAmplitude) : null;
                EventClockSimulation? events = kernel == "canonical" ? null : new(state, dynamicsSeed, manifest.FluctuationAmplitude,
                    kernel == "border" ? ProposalSpaceKind.BorderSites : ProposalSpaceKind.DirectedInterface);
                long accepted = 0, noOps = 0, hard = 0, energy = 0, wall = 0;
                for (int mcs = 0; mcs <= manifest.McsCount; mcs++)
                {
                    if (mcs > 0)
                    {
                        if (canonical is not null)
                        {
                            for (int slot = 0; slot < state.SiteCount; slot++)
                            {
                                AttemptResult result = canonical.Attempt();
                                if (result.Status == AttemptStatus.Accepted) accepted++;
                                else if (result.Status == AttemptStatus.NoOp) noOps++;
                                else if (result.RejectionReason == RejectionReason.FixedWall) wall++;
                                else if (result.RejectionReason == RejectionReason.Metropolis) energy++;
                                else hard++;
                            }
                        }
                        else
                        {
                            events!.RunMcs();
                            accepted = events.AcceptedCopies; noOps = events.NoOpProposals;
                            hard = events.HardConstraintRejections; energy = events.EnergyRejections; wall = events.FixedWallRejections;
                        }
                    }
                    if (mcs is not (0 or 10 or 20 or 40 or 80)) continue;
                    InvariantValidator.Validate(state);
                    TissueMeasurements tissue = TissueMeasurementCalculator.Measure(state, manifest.Initialiser.TypeAId,
                        manifest.Initialiser.TypeBId, manifest.Measurements.InterfaceNeighbourhood);
                    double[] areas = new double[manifest.Initialiser.CellCount];
                    double[] perimeters = new double[areas.Length];
                    double[] shapes = new double[areas.Length];
                    for (int id = 1; id <= areas.Length; id++)
                    {
                        CellState cell = state.GetCellState(id);
                        areas[id - 1] = cell.Area; perimeters[id - 1] = cell.Perimeter;
                        shapes[id - 1] = cell.Perimeter == 0 ? 0 : (double)cell.Area / ((double)cell.Perimeter * cell.Perimeter);
                        Write(cells, condition, kernel, replicate, mcs, id, cell.CellTypeId, cell.Area, cell.Perimeter, shapes[id - 1]);
                    }
                    Array.Sort(areas); Array.Sort(perimeters);
                    var domainA = tissue.DomainsByType.Single(domain => domain.TypeId == manifest.Initialiser.TypeAId);
                    var domainB = tissue.DomainsByType.Single(domain => domain.TypeId == manifest.Initialiser.TypeBId);
                    long attempts = (long)mcs * state.SiteCount;
                    Write(samples, condition, kernel, replicate, initialisationSeed, dynamicsSeed, initialHash.ToString("X16", CultureInfo.InvariantCulture), mcs,
                        tissue.HeterotypicInterfaceFraction, tissue.HeterotypicInterfaceCount, tissue.TotalCellCellInterfaceCount,
                        tissue.HomotypicInterfacesByType.Single(item => item.TypeId == manifest.Initialiser.TypeAId).Count,
                        tissue.HomotypicInterfacesByType.Single(item => item.TypeId == manifest.Initialiser.TypeBId).Count,
                        domainA.DomainCount, domainB.DomainCount, domainA.LargestDomainCellCount, domainB.LargestDomainCellCount,
                        areas.Average(), StandardDeviation(areas), Quantile(areas, 0.1), Quantile(areas, 0.5), Quantile(areas, 0.9),
                        perimeters.Average(), StandardDeviation(perimeters), Quantile(perimeters, 0.1), Quantile(perimeters, 0.5), Quantile(perimeters, 0.9),
                        shapes.Average(), areas.Sum() / state.SiteCount, attempts == 0 ? 0 : (double)accepted / attempts,
                        attempts, canonical?.AttemptCount ?? events!.RawEventProposals, noOps, hard, energy, wall,
                        canonical?.ConnectivityFallbackCount ?? events!.ConnectivityFallbacks,
                        events?.BorderSiteCount ?? -1, events?.DirectedInterfaceCount ?? -1);
                    if (accepted + noOps + hard + energy + wall != attempts)
                        throw new InvalidOperationException("Diagnostic attempt accounting differs from the clock.");
                }
            }
            samples.Flush(); cells.Flush();
            Console.WriteLine($"Completed {kernel}: {condition}, {seeds} paired seeds.");
        }
    }

    private static IEnumerable<(string, ExperimentManifest)> Conditions(int seeds)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Scenarios");
        ExperimentManifest sorting = ExperimentManifest.ReadJson(Path.Combine(root, "E02-sorting.json"));
        ExperimentManifest control = ExperimentManifest.ReadJson(Path.Combine(root, "E02-control.json"));
        ExperimentManifest high = ExperimentManifest.ReadJson(Path.Combine(root, "E01-fluctuation-high.json"));
        yield return ("sorting-32-t6", Resolve(sorting, "sorting-32-t6", 32, 6, false, seeds));
        yield return ("sorting-32-t12", Resolve(sorting, "sorting-32-t12", 32, 12, false, seeds));
        yield return ("sorting-32-t24", Resolve(sorting, "sorting-32-t24", 32, 24, false, seeds));
        yield return ("sorting-64-t12", Resolve(sorting, "sorting-64-t12", 64, 12, false, seeds));
        yield return ("control-32-t12", Resolve(control, "control-32-t12", 32, 12, false, seeds));
        yield return ("control-64-t12", Resolve(control, "control-64-t12", 64, 12, false, seeds));
        yield return ("sorting-wall-32-t12", Resolve(sorting, "sorting-wall-32-t12", 32, 12, true, seeds));
        yield return ("high-32-t24", high with { ReplicateCount = seeds });
    }

    private static ExperimentManifest Resolve(ExperimentManifest source, string id, int width, double fluctuation, bool wall, int seeds) => source with
    {
        ExperimentId = id,
        GridWidth = width,
        GridHeight = width,
        McsCount = 80,
        ReplicateCount = seeds,
        Initialiser = source.Initialiser with { CellCount = width == 64 ? 64 : 16 },
        FluctuationAmplitude = fluctuation,
        BoundaryMode = wall ? BoundaryMode.Wall : BoundaryMode.Periodic
    };

    private static double Quantile(double[] sorted, double fraction)
    {
        double index = (sorted.Length - 1) * fraction;
        int lower = (int)index;
        int upper = Math.Min(lower + 1, sorted.Length - 1);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (index - lower);
    }

    private static double StandardDeviation(double[] values)
    {
        double mean = values.Average();
        return Math.Sqrt(values.Sum(value => (value - mean) * (value - mean)) / values.Length);
    }

    private static ulong Hash(int[] ids)
    {
        ulong hash = 14695981039346656037UL;
        foreach (int id in ids) hash = unchecked((hash ^ (ulong)id) * 1099511628211UL);
        return hash;
    }

    private static void Write(StreamWriter writer, params object[] values) => writer.WriteLine(string.Join(',',
        values.Select(value => value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString())));
}
