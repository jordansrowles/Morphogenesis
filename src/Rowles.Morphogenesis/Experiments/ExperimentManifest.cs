using System.Text.Json;
using System.Text.Json.Serialization;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Experiments.Configuration;

namespace Rowles.Morphogenesis.Experiments;

/// <summary>A complete, versioned description of one reproducible serial CPM experiment.</summary>
public sealed record ExperimentManifest
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    public required int SchemaVersion { get; init; }

    public required string ExperimentId { get; init; }

    public required string Name { get; init; }

    public required int GridWidth { get; init; }

    public required int GridHeight { get; init; }

    public required BoundaryMode BoundaryMode { get; init; }

    public required CopyNeighbourhood CopyNeighbourhood { get; init; }

    public required ContactCouplingNeighbourhood ContactCouplingNeighbourhood { get; init; }

    public required PerimeterNeighbourhood PerimeterNeighbourhood { get; init; }

    public required ConnectivityAdjacency ConnectivityAdjacency { get; init; }

    public required ExperimentKernel Kernel { get; init; }

    public required ulong BaseSeed { get; init; }

    public required int McsCount { get; init; }

    public required InitialiserConfiguration Initialiser { get; init; }

    public required CellTypeDefinition[] CellTypes { get; init; }

    /// <summary>Square contact-energy matrix indexed by cell type ID.</summary>
    public required double[][] ContactEnergies { get; init; }

    public required CellMechanicsConfiguration Mechanics { get; init; }

    public required double FluctuationAmplitude { get; init; }

    public required MeasurementConfiguration Measurements { get; init; }

    public required int ReplicateCount { get; init; }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new NotSupportedException($"Experiment schema version {SchemaVersion} is not supported; expected {CurrentSchemaVersion}.");
        }

        if (!IsIdentifier(ExperimentId))
        {
            throw new ArgumentException("Experiment ID must contain only letters, digits, dots, underscores, or hyphens.", nameof(ExperimentId));
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("Experiment name is required.", nameof(Name));
        }

        if (GridWidth <= 0 || GridHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(GridWidth), "Grid dimensions must be positive.");
        }

        int siteCount = checked(GridWidth * GridHeight);
        if (!Enum.IsDefined(BoundaryMode) ||
            !Enum.IsDefined(CopyNeighbourhood) ||
            !Enum.IsDefined(ContactCouplingNeighbourhood) ||
            !Enum.IsDefined(PerimeterNeighbourhood) ||
            !Enum.IsDefined(ConnectivityAdjacency) ||
            !Enum.IsDefined(Kernel))
        {
            throw new ArgumentException("The manifest contains an unsupported boundary, neighbourhood, or kernel identifier.");
        }

        if (Kernel != ExperimentKernel.CanonicalSerial)
        {
            throw new NotSupportedException($"Kernel '{Kernel}' is not supported.");
        }

        if (McsCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(McsCount), "MCS count cannot be negative.");
        }

        if (ReplicateCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ReplicateCount), "At least one replicate is required.");
        }

        ArgumentNullException.ThrowIfNull(Initialiser);
        ArgumentNullException.ThrowIfNull(Mechanics);
        ArgumentNullException.ThrowIfNull(Measurements);
        ArgumentNullException.ThrowIfNull(CellTypes);
        ArgumentNullException.ThrowIfNull(ContactEnergies);

        if (!Enum.IsDefined(Initialiser.Kind) || Initialiser.Kind != InitialiserKind.PackedAggregate)
        {
            throw new ArgumentException("The initialiser kind is unsupported.", nameof(Initialiser));
        }

        if (Initialiser.CellCount <= 0 || Initialiser.CellCount > siteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(Initialiser), "Cell count must be positive and cannot exceed the lattice site count.");
        }

        if (Initialiser.ApproximateTargetCellArea <= 0 || Initialiser.ApproximateTargetCellArea > siteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(Initialiser), "Approximate target cell area must be positive and fit on the lattice.");
        }

        if (!double.IsFinite(Initialiser.TypeAProportion) || Initialiser.TypeAProportion < 0 || Initialiser.TypeAProportion > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Initialiser), "Type A proportion must be finite and in [0, 1].");
        }

        if (!double.IsFinite(Mechanics.TargetArea) || Mechanics.TargetArea < 0 ||
            !double.IsFinite(Mechanics.AreaStiffness) || Mechanics.AreaStiffness < 0 ||
            !double.IsFinite(Mechanics.TargetPerimeter) || Mechanics.TargetPerimeter < 0 ||
            !double.IsFinite(Mechanics.PerimeterStiffness) || Mechanics.PerimeterStiffness < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Mechanics), "Cell targets and stiffnesses must be finite and non-negative.");
        }

        if (!double.IsFinite(FluctuationAmplitude) || FluctuationAmplitude < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(FluctuationAmplitude), "Fluctuation amplitude must be finite and non-negative.");
        }

        ValidateCellTypes();
        ValidateContactEnergies();
        ValidateMeasurements();
        ValidateInitialiserFits();
    }

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    public static JsonSerializerOptions CreateSerializerOptions() => new(SerializerOptions);

    public static ExperimentManifest FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        ExperimentManifest manifest = JsonSerializer.Deserialize<ExperimentManifest>(json, SerializerOptions)
            ?? throw new JsonException("The experiment manifest was JSON null.");
        manifest.Validate();
        return manifest;
    }

    public static ExperimentManifest ReadJson(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromJson(File.ReadAllText(path));
    }

    private static bool IsIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private void ValidateCellTypes()
    {
        if (CellTypes.Length < 3)
        {
            throw new ArgumentException("At least medium, type A, and type B definitions are required.", nameof(CellTypes));
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < CellTypes.Length; index++)
        {
            CellTypeDefinition? type = CellTypes[index];
            if (type is null || type.TypeId != index)
            {
                throw new ArgumentException("Cell type definitions must be ordered by contiguous type ID from zero.", nameof(CellTypes));
            }

            if (string.IsNullOrWhiteSpace(type.Name) || !names.Add(type.Name.Trim()))
            {
                throw new ArgumentException("Cell type names must be non-empty and unique.", nameof(CellTypes));
            }
        }

        if (CellTypes[0].TypeId != 0 ||
            !CellTypes[0].Name.Equals("Medium", StringComparison.OrdinalIgnoreCase) ||
            Initialiser.TypeAId <= 0 || Initialiser.TypeAId >= CellTypes.Length ||
            Initialiser.TypeBId <= 0 || Initialiser.TypeBId >= CellTypes.Length ||
            Initialiser.TypeAId == Initialiser.TypeBId)
        {
            throw new ArgumentException("Type 0 must be medium and initialiser A/B IDs must name distinct biological types.", nameof(CellTypes));
        }
    }

    private void ValidateContactEnergies()
    {
        if (ContactEnergies.Length != CellTypes.Length)
        {
            throw new ArgumentException("Contact-energy matrix size must equal the number of cell types.", nameof(ContactEnergies));
        }

        for (int row = 0; row < ContactEnergies.Length; row++)
        {
            double[]? values = ContactEnergies[row];
            if (values is null || values.Length != ContactEnergies.Length)
            {
                throw new ArgumentException("Contact-energy matrix must be square and match the cell type count.", nameof(ContactEnergies));
            }

        }

        for (int row = 0; row < ContactEnergies.Length; row++)
        {
            for (int column = 0; column < ContactEnergies.Length; column++)
            {
                if (!double.IsFinite(ContactEnergies[row][column]) || ContactEnergies[row][column] != ContactEnergies[column][row])
                {
                    throw new ArgumentException("Contact energies must be finite and symmetric.", nameof(ContactEnergies));
                }
            }
        }
    }

    private void ValidateMeasurements()
    {
        if (Measurements.EveryMcs <= 0 || Measurements.SnapshotEveryMcs < 0 || Measurements.ValidateInvariantsEveryMcs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Measurements), "Measurement cadence must be positive; snapshot and invariant cadences cannot be negative.");
        }

        if (!Enum.IsDefined(Measurements.InterfaceNeighbourhood))
        {
            throw new ArgumentException("Measurement neighbourhood is unsupported.", nameof(Measurements));
        }

        if (BoundaryMode == BoundaryMode.Periodic)
        {
            const int minimumDimension = 3;
            if (GridWidth < minimumDimension || GridHeight < minimumDimension)
            {
                throw new ArgumentException("Periodic dimensions alias one of the configured neighbourhood stencils.", nameof(Measurements));
            }
        }
    }

    private void ValidateInitialiserFits()
    {
        int cellWidth = checked((int)Math.Ceiling(Math.Sqrt(Initialiser.ApproximateTargetCellArea)));
        int cellHeight = checked((int)Math.Ceiling((double)Initialiser.ApproximateTargetCellArea / cellWidth));
        bool fits = false;
        for (int columns = 1; columns <= Initialiser.CellCount; columns++)
        {
            int rows = (int)(((long)Initialiser.CellCount + columns - 1) / columns);
            if ((long)columns * cellWidth <= GridWidth && (long)rows * cellHeight <= GridHeight)
            {
                fits = true;
                break;
            }

            if ((long)columns * cellHeight <= GridWidth && (long)rows * cellWidth <= GridHeight)
            {
                fits = true;
                break;
            }
        }

        if (!fits)
        {
            throw new ArgumentException("The requested packed aggregate cannot fit on this lattice.", nameof(Initialiser));
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        options.Converters.Add(new JsonStringEnumConverter<BoundaryMode>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<CopyNeighbourhood>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<ContactCouplingNeighbourhood>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<PerimeterNeighbourhood>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<ConnectivityAdjacency>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<ExperimentKernel>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<InitialiserKind>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
