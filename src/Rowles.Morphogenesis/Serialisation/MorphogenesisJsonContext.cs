using System.Text.Json.Serialization;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Results;
using Rowles.Morphogenesis.Snapshots;

namespace Rowles.Morphogenesis.Serialisation;

[JsonSerializable(typeof(ExperimentManifest))]
[JsonSerializable(typeof(ExperimentSnapshot))]
[JsonSerializable(typeof(ExperimentEnsembleResult))]
internal partial class MorphogenesisJsonContext : JsonSerializerContext
{
    internal static MorphogenesisJsonContext Instance { get; } = new(global::Rowles.Morphogenesis.Experiments.ExperimentManifest.CreateSerialiserOptions());
}
