using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Lattice;

namespace Rowles.Morphogenesis.Tests.Experiments;

internal static class ExperimentManifestFactory
{
    internal static ExperimentManifest Create() => new()
    {
        SchemaVersion = ExperimentManifest.CurrentSchemaVersion,
        ExperimentId = "test-sorting",
        Name = "Test sorting experiment",
        GridWidth = 16,
        GridHeight = 16,
        BoundaryMode = BoundaryMode.Wall,
        CopyNeighbourhood = CopyNeighbourhood.VonNeumann,
        ContactCouplingNeighbourhood = ContactCouplingNeighbourhood.Moore,
        PerimeterNeighbourhood = PerimeterNeighbourhood.Moore,
        ConnectivityAdjacency = ConnectivityAdjacency.VonNeumann,
        Kernel = ExperimentKernel.CanonicalSerial,
        BaseSeed = 123456,
        McsCount = 4,
        Initialiser = new InitialiserConfiguration
        {
            Kind = InitialiserKind.PackedAggregate,
            CellCount = 4,
            ApproximateTargetCellArea = 4,
            TypeAId = 1,
            TypeBId = 2,
            TypeAProportion = 0.5
        },
        CellTypes =
        [
            new CellTypeDefinition { TypeId = 0, Name = "Medium" },
            new CellTypeDefinition { TypeId = 1, Name = "A" },
            new CellTypeDefinition { TypeId = 2, Name = "B" }
        ],
        ContactEnergies =
        [
            [0, 8, 8],
            [8, 4, 4],
            [8, 4, 4]
        ],
        Mechanics = new CellMechanicsConfiguration
        {
            TargetArea = 4,
            AreaStiffness = 1,
            TargetPerimeter = 20,
            PerimeterStiffness = 0.1
        },
        FluctuationAmplitude = 8,
        Measurements = new MeasurementConfiguration
        {
            EveryMcs = 2,
            IncludeMcsZero = true,
            InterfaceNeighbourhood = ContactCouplingNeighbourhood.VonNeumann,
            SnapshotEveryMcs = 0,
            ValidateInvariantsEveryMcs = 2
        },
        ReplicateCount = 3
    };
}
