using System.Text.Json;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Initialisation;
using Rowles.Morphogenesis.Snapshots;
using Rowles.Morphogenesis.Tests.Experiments;

namespace Rowles.Morphogenesis.Tests.Snapshots;

public sealed class ExperimentSnapshotTests
{
    [Fact]
    public void Snapshot_json_round_trip_preserves_shape_occupancy_and_type_mapping()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create();
        PackedAggregateInitialisation initialisation = PackedAggregateInitialiser.Create(manifest, 17);
        ExperimentSnapshot snapshot = ExperimentSnapshot.Capture(manifest, "test-sorting-r0001", 0, 17, 0, initialisation.State);

        ExperimentSnapshot actual = ExperimentSnapshot.FromJson(snapshot.ToJson());

        Assert.Equal(snapshot.SnapshotSchemaVersion, actual.SnapshotSchemaVersion);
        Assert.Equal(snapshot.ModelVersion, actual.ModelVersion);
        Assert.Equal(snapshot.ExperimentId, actual.ExperimentId);
        Assert.Equal(snapshot.ReplicateId, actual.ReplicateId);
        Assert.Equal(snapshot.Seed, actual.Seed);
        Assert.Equal(snapshot.Mcs, actual.Mcs);
        Assert.Equal(manifest.GridWidth * manifest.GridHeight, actual.CellIds.Length);
        Assert.Equal(manifest.Initialiser.CellCount, actual.Cells.Length);
        Assert.Equal(manifest.CellTypes.Select(type => type.Name), actual.CellTypes.Select(type => type.Name));
        Assert.Equal(snapshot.CellIds, actual.CellIds);
        Assert.Equal(JsonSerializer.Serialize(snapshot.Cells), JsonSerializer.Serialize(actual.Cells));
        actual.Validate();
    }

    [Fact]
    public void Snapshot_with_inconsistent_dimensions_or_unknown_schema_is_rejected()
    {
        ExperimentManifest manifest = ExperimentManifestFactory.Create();
        PackedAggregateInitialisation initialisation = PackedAggregateInitialiser.Create(manifest, 17);
        ExperimentSnapshot snapshot = ExperimentSnapshot.Capture(manifest, "test-sorting-r0001", 0, 17, 0, initialisation.State);

        Assert.Throws<ArgumentException>(() => (snapshot with { GridWidth = snapshot.GridWidth - 1 }).Validate());
        Assert.Throws<NotSupportedException>(() => (snapshot with { SnapshotSchemaVersion = 2 }).Validate());
    }
}
