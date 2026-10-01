using System.Text.Json.Serialization;

namespace Rowles.Evolution.Algorithms;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(EvolutionCheckpoint))]
internal partial class EvolutionJsonContext : JsonSerializerContext
{
}
