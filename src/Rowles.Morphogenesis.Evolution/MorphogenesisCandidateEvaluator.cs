using System.Diagnostics;
using Rowles.Evolution.Evaluation;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Configuration;
using Rowles.Morphogenesis.Experiments.Execution;
using Rowles.Morphogenesis.Experiments.Results;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Measurements;
using Rowles.Morphogenesis.Measurements.Metrics;

namespace Rowles.Morphogenesis.Optimisation;

/// <summary>Fixed-workload headless adapter for the M2 experiment runner.</summary>
public sealed class MorphogenesisCandidateEvaluator
{
    public const ulong CampaignSeed = 20260929;
    public const int ReplicateCount = 2;
    private const int TypeAId = 1;
    private const int TypeBId = 2;
    private readonly ExperimentManifest template;
    private readonly Func<ExperimentManifest, ExperimentReplicateResult[]> runReplicates;

    public MorphogenesisCandidateEvaluator(ExperimentManifest template)
        : this(template, manifest => ExperimentRunner.Run(manifest).Replicates)
    {
    }

    internal MorphogenesisCandidateEvaluator(ExperimentManifest template,
        Func<ExperimentManifest, ExperimentReplicateResult[]> runReplicates)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(runReplicates);
        template.Validate();
        this.template = template with { ContactEnergies = CloneMatrix(template.ContactEnergies) };
        this.runReplicates = runReplicates;
    }

    public static ExperimentManifest CreateE02Template() => new()
    {
        SchemaVersion = ExperimentManifest.CurrentSchemaVersion,
        ExperimentId = "m25-e02",
        Name = "M2.5 fixed E02 quality-diversity workload",
        GridWidth = 32,
        GridHeight = 32,
        BoundaryMode = BoundaryMode.Periodic,
        CopyNeighbourhood = CopyNeighbourhood.VonNeumann,
        ContactCouplingNeighbourhood = ContactCouplingNeighbourhood.Moore,
        PerimeterNeighbourhood = PerimeterNeighbourhood.Moore,
        ConnectivityAdjacency = ConnectivityAdjacency.VonNeumann,
        Kernel = ExperimentKernel.CanonicalSerial,
        BaseSeed = CampaignSeed,
        McsCount = 80,
        Initialiser = new InitialiserConfiguration
        {
            Kind = InitialiserKind.PackedAggregate,
            CellCount = 16,
            ApproximateTargetCellArea = 16,
            TypeAId = TypeAId,
            TypeBId = TypeBId,
            TypeAProportion = 0.5
        },
        CellTypes =
        [
            new CellTypeDefinition { TypeId = 0, Name = "Medium" },
            new CellTypeDefinition { TypeId = 1, Name = "A" },
            new CellTypeDefinition { TypeId = 2, Name = "B" }
        ],
        ContactEnergies = [[0, 10, 10], [10, 2, 2], [10, 2, 2]],
        Mechanics = new CellMechanicsConfiguration
        {
            TargetArea = 16,
            AreaStiffness = 1.0,
            TargetPerimeter = 44,
            PerimeterStiffness = 0.1
        },
        FluctuationAmplitude = 12,
        Measurements = new MeasurementConfiguration
        {
            EveryMcs = 10,
            IncludeMcsZero = true,
            InterfaceNeighbourhood = ContactCouplingNeighbourhood.Moore,
            SnapshotEveryMcs = 0,
            ValidateInvariantsEveryMcs = 10
        },
        ReplicateCount = 16
    };

    public MorphogenesisEvaluationResult Evaluate(NumericCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (candidate.Values.Count != 3 || candidate.Values.Any(value => !double.IsFinite(value)) ||
            candidate.Values[0] is < 0 or > 20 || candidate.Values[1] is < 0 or > 30 || candidate.Values[2] is < 0 or > 24)
            return Invalid("candidate-out-of-bounds");

        ExperimentManifest resolved;
        try
        {
            resolved = ResolveManifest(candidate);
        }
        catch (ArgumentException exception)
        {
            return Invalid($"manifest-validation: {exception.GetType().Name}");
        }
        catch (NotSupportedException exception)
        {
            return Invalid($"manifest-validation: {exception.GetType().Name}");
        }

        ExperimentReplicateResult[] replicates = runReplicates(resolved);
        if (replicates.Length != ReplicateCount || replicates.Any(rep => rep.Status != ReplicateRunStatus.Succeeded))
        {
            string reason = string.Join(";", replicates.Where(rep => rep.Status != ReplicateRunStatus.Succeeded)
                .Select(rep => rep.Failure ?? "replicate-failed").Distinct(StringComparer.Ordinal));
            return Invalid(string.IsNullOrWhiteSpace(reason) ? "replicate-count-mismatch" : $"replicate-failed: {reason}",
                replicates.Select(rep => rep.ElapsedMilliseconds).ToArray());
        }

        double[] retention = new double[ReplicateCount];
        double[] heterotypic = new double[ReplicateCount];
        double[] fragmentation = new double[ReplicateCount];
        for (int i = 0; i < ReplicateCount; i++)
        {
            ExperimentReplicateResult replicate = replicates[i];
            MeasurementSample? initial = replicate.Samples.FirstOrDefault(sample => sample.Mcs == 0);
            TissueMeasurements? final = replicate.FinalMeasurements;
            if (initial?.Metrics is null || final is null || initial.Metrics.TotalCellCellInterfaceCount <= 0)
                return Invalid("zero-or-missing-initial-interface", replicates.Select(rep => rep.ElapsedMilliseconds).ToArray());

            retention[i] = (double)final.TotalCellCellInterfaceCount / initial.Metrics.TotalCellCellInterfaceCount;
            heterotypic[i] = final.HeterotypicInterfaceFraction;
            int biologicalCells = final.TypeACellCount + final.TypeBCellCount;
            long domainCount = final.DomainsByType.Where(domain => domain.TypeId is TypeAId or TypeBId).Sum(domain => (long)domain.DomainCount);
            if (biologicalCells <= 0) return Invalid("zero-biological-cell-denominator", replicates.Select(rep => rep.ElapsedMilliseconds).ToArray());
            fragmentation[i] = (double)domainCount / biologicalCells;
        }

        double objective = retention.Average();
        double descriptor0 = heterotypic.Average();
        double descriptor1 = fragmentation.Average();
        if (!double.IsFinite(objective) || !InUnitInterval(descriptor0) || !InUnitInterval(descriptor1))
            return Invalid("non-finite-or-out-of-range-result", replicates.Select(rep => rep.ElapsedMilliseconds).ToArray());

        stopwatch.Stop();
        return new MorphogenesisEvaluationResult(CandidateEvaluation.Valid(candidate.CandidateId, objective, [descriptor0, descriptor1]),
            stopwatch.Elapsed.TotalMilliseconds, Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore),
            replicates.Select(rep => rep.ElapsedMilliseconds).ToArray());

        MorphogenesisEvaluationResult Invalid(string reason, double[]? replicateTimes = null)
        {
            stopwatch.Stop();
            return new MorphogenesisEvaluationResult(CandidateEvaluation.Invalid(candidate.CandidateId, reason),
                stopwatch.Elapsed.TotalMilliseconds, Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore),
                replicateTimes ?? []);
        }
    }

    /// <summary>Creates and validates an isolated candidate manifest without changing template-owned arrays.</summary>
    public ExperimentManifest ResolveManifest(NumericCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Values.Count != 3 || candidate.Values.Any(value => !double.IsFinite(value)) ||
            candidate.Values[0] is < 0 or > 20 || candidate.Values[1] is < 0 or > 30 || candidate.Values[2] is < 0 or > 24)
            throw new ArgumentOutOfRangeException(nameof(candidate), "Candidate values must have three finite values inside the M2.5 bounds.");

        double[][] contactEnergies = CloneMatrix(template.ContactEnergies);
        contactEnergies[TypeAId][TypeAId] = candidate.Values[0];
        contactEnergies[TypeBId][TypeBId] = candidate.Values[0];
        contactEnergies[TypeAId][TypeBId] = candidate.Values[1];
        contactEnergies[TypeBId][TypeAId] = candidate.Values[1];
        ExperimentManifest resolved = template with
        {
            ContactEnergies = contactEnergies,
            FluctuationAmplitude = candidate.Values[2],
            BaseSeed = CampaignSeed,
            ReplicateCount = ReplicateCount,
            Measurements = template.Measurements with
            {
                EveryMcs = checked(template.McsCount + 1),
                IncludeMcsZero = true,
                SnapshotEveryMcs = 0,
                ValidateInvariantsEveryMcs = 0
            }
        };
        resolved.Validate();
        return resolved;
    }

    private static double[][] CloneMatrix(double[][] matrix) => matrix.Select(row => row.ToArray()).ToArray();
    private static bool InUnitInterval(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
}
