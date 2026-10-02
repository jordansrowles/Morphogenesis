using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rowles.Morphogenesis.Benchmarks.Analysis;

internal static class EventEnsembleProtocol
{
    internal const int Version = 1;
    internal const int BootstrapReplicates = 512;
    internal const string BootstrapKeyFormat = "{condition}:{metric}:{kernel}:{seeds}";
    internal const string BootstrapSeedRule = "First 64 bits of SHA-256(key), interpreted as a big-endian unsigned integer";
    internal const double ConfidenceLevel = 0.90;
    internal const double MeanBandStandardDeviations = 0.5;
    internal const double VarianceRatioLowerBound = 0.5;
    internal const double VarianceRatioUpperBound = 2.0;

    internal static readonly string[] Metrics =
    [
        "mixing", "heterotypic", "total_interface", "homotypic_a", "homotypic_b",
        "domains_a", "domains_b", "largest_domain_a", "largest_domain_b",
        "area_mean", "area_sd", "area_q10", "area_q50", "area_q90",
        "perimeter_mean", "perimeter_sd", "perimeter_q10", "perimeter_q50", "perimeter_q90",
        "shape_mean", "occupied_fraction", "acceptance_fraction"
    ];

    internal static readonly string[] Conditions =
    [
        "sorting-32-t6", "sorting-32-t12", "sorting-32-t24", "sorting-64-t12",
        "control-32-t12", "control-64-t12", "sorting-wall-32-t12", "high-32-t24"
    ];

    internal static int[] Checkpoints(string condition) =>
        condition == "high-32-t24" ? [0, 10, 20, 40] : [0, 10, 20, 40, 80];

    internal static ulong BootstrapSeed(string key)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return BinaryPrimitives.ReadUInt64BigEndian(digest);
    }

    internal static string BootstrapKey(string condition, string metric, string kernel, int seedCount) =>
        string.Concat(condition, ":", metric, ":", kernel, ":", seedCount.ToString(CultureInfo.InvariantCulture));

    internal static double StudentCriticalValue(int sampleCount) => sampleCount switch
    {
        64 => 1.669402221706,
        128 => 1.656940719756,
        256 => 1.650850024,
        _ => throw new ArgumentOutOfRangeException(nameof(sampleCount), "Qualification requires 64, 128, or 256 paired seeds.")
    };
}
