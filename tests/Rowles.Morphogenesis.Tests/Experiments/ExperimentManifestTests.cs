using System.Text.Json;
using System.Text.Json.Nodes;
using Rowles.Morphogenesis.Experiments;

namespace Rowles.Morphogenesis.Tests.Experiments;

public sealed class ExperimentManifestTests
{
    [Fact]
    public void Json_round_trip_preserves_the_resolved_experiment()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create();

        ExperimentManifest actual = ExperimentManifest.FromJson(manifest.ToJson());

        Assert.Equal(manifest.ToJson(), actual.ToJson());
        Assert.Equal(ExperimentManifest.CurrentSchemaVersion, actual.SchemaVersion);
        Assert.Equal(manifest.ContactEnergies[1][2], actual.ContactEnergies[1][2]);
        Assert.Equal(manifest.Measurements.InterfaceNeighbourhood, actual.Measurements.InterfaceNeighbourhood);
    }

    [Fact]
    public void Missing_required_fields_are_rejected()
    {
        JsonObject json = ParseManifest();
        json.Remove("baseSeed");

        Assert.Throws<JsonException>(() => ExperimentManifest.FromJson(json.ToJsonString()));
    }

    [Fact]
    public void Unknown_enum_values_are_rejected()
    {
        JsonObject json = ParseManifest();
        json["boundaryMode"] = "unbounded";

        Assert.Throws<JsonException>(() => ExperimentManifest.FromJson(json.ToJsonString()));
    }

    [Fact]
    public void Unsupported_kernel_identifiers_are_rejected()
    {
        JsonObject json = ParseManifest();
        json["kernel"] = "parallelExperimental";

        Assert.Throws<JsonException>(() => ExperimentManifest.FromJson(json.ToJsonString()));
    }

    [Fact]
    public void Unknown_json_fields_are_rejected()
    {
        JsonObject json = ParseManifest();
        json["hiddenDefault"] = 1;

        Assert.Throws<JsonException>(() => ExperimentManifest.FromJson(json.ToJsonString()));
    }

    [Fact]
    public void Unsupported_schema_versions_are_rejected()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create() with { SchemaVersion = 2 };
        JsonObject json = ParseManifest();
        json["schemaVersion"] = 2;

        Assert.Throws<NotSupportedException>(manifest.Validate);
        Assert.Throws<NotSupportedException>(() => ExperimentManifest.FromJson(json.ToJsonString()));
    }

    [Fact]
    public void Non_finite_scientific_values_are_rejected()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create() with { FluctuationAmplitude = double.NaN };
        ExperimentManifest nonFiniteMatrix = ExperimentManifestFactory.Create() with
        {
            ContactEnergies =
            [
                [0, 8, 8],
                [8, 4, double.PositiveInfinity],
                [8, double.PositiveInfinity, 4]
            ]
        };

        Assert.Throws<ArgumentOutOfRangeException>(manifest.Validate);
        Assert.Throws<ArgumentException>(nonFiniteMatrix.Validate);
    }

    [Fact]
    public void Invalid_contact_matrix_dimensions_and_symmetry_are_rejected()
    {
        ExperimentManifest wrongSize = ExperimentManifestFactory.Create() with
        {
            ContactEnergies = [[0, 1], [1, 0]]
        };
        ExperimentManifest asymmetric = ExperimentManifestFactory.Create() with
        {
            ContactEnergies =
            [
                [0, 8, 8],
                [8, 4, 3],
                [8, 4, 4]
            ]
        };

        Assert.Throws<ArgumentException>(wrongSize.Validate);
        Assert.Throws<ArgumentException>(asymmetric.Validate);
    }

    [Fact]
    public void Duplicate_cell_type_identifiers_are_rejected()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create() with
        {
            CellTypes =
            [
                new CellTypeDefinition { TypeId = 0, Name = "Medium" },
                new CellTypeDefinition { TypeId = 1, Name = "A" },
                new CellTypeDefinition { TypeId = 1, Name = "B" }
            ]
        };

        Assert.Throws<ArgumentException>(manifest.Validate);
    }

    [Fact]
    public void Cell_type_definitions_must_be_ordered_by_type_identifier()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create() with
        {
            CellTypes =
            [
                new CellTypeDefinition { TypeId = 0, Name = "Medium" },
                new CellTypeDefinition { TypeId = 2, Name = "B" },
                new CellTypeDefinition { TypeId = 1, Name = "A" }
            ]
        };

        Assert.Throws<ArgumentException>(manifest.Validate);
    }

    [Fact]
    public void Impossible_aggregate_geometry_is_rejected_before_simulation_construction()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create() with
        {
            GridWidth = 3,
            GridHeight = 3,
            Initialiser = ExperimentManifestFactory.Create().Initialiser with { CellCount = 8 }
        };

        Assert.Throws<ArgumentException>(manifest.Validate);
    }

    [Fact]
    public void Periodic_stencil_aliases_are_rejected_during_manifest_validation()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create() with
        {
            BoundaryMode = Rowles.Morphogenesis.Lattice.BoundaryMode.Periodic,
            GridWidth = 2
        };

        Assert.Throws<ArgumentException>(manifest.Validate);
    }

    private static JsonObject ParseManifest() =>
        JsonNode.Parse(ExperimentManifestFactory.Create().ToJson())!.AsObject();
}
