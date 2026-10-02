using System.Security.Cryptography;
using System.Text;
using Rowles.Morphogenesis.Benchmarks.Analysis;

namespace Rowles.Morphogenesis.Tests.Analysis;

public sealed class EventEnsembleProtocolTests
{
    [Fact]
    public void Analysis_protocol_hash_pins_the_declared_protocol_payload()
    {
        string[] fields =
        [
            "protocol-version=2",
            "confidence-level=0.9",
            "student-critical-64=1.669402221706",
            "student-critical-128=1.656940719756",
            "student-critical-256=1.650850024",
            "mean-band-standard-deviations=0.5",
            "variance-ratio-lower-bound=0.5",
            "variance-ratio-upper-bound=2",
            "variance-bootstrap-replicates=512",
            "variance-bootstrap-lower-quantile=0.05",
            "variance-bootstrap-upper-quantile=0.95",
            "bootstrap-key-format={condition}:{metric}:{kernel}:{seeds}",
            "bootstrap-seed-rule=First 64 bits of SHA-256(key), interpreted as a big-endian unsigned integer",
            "bootstrap-rng=MT19937-init-by-array-python-compatible-v1",
            "zero-canonical-zero-candidate-rule=ratio-and-interval-1-no-bootstrap",
            "zero-canonical-nonzero-candidate-rule=ratio-and-interval-positive-infinity-no-bootstrap",
            "metrics=mixing,heterotypic,total_interface,homotypic_a,homotypic_b,domains_a,domains_b,largest_domain_a,largest_domain_b,area_mean,area_sd,area_q10,area_q50,area_q90,perimeter_mean,perimeter_sd,perimeter_q10,perimeter_q50,perimeter_q90,shape_mean,occupied_fraction,acceptance_fraction",
            "conditions=sorting-32-t6,sorting-32-t12,sorting-32-t24,sorting-64-t12,control-32-t12,control-64-t12,sorting-wall-32-t12,high-32-t24",
            "checkpoints-per-condition=sorting-32-t6:0,10,20,40,80;sorting-32-t12:0,10,20,40,80;sorting-32-t24:0,10,20,40,80;sorting-64-t12:0,10,20,40,80;control-32-t12:0,10,20,40,80;control-64-t12:0,10,20,40,80;sorting-wall-32-t12:0,10,20,40,80;high-32-t24:0,10,20,40"
        ];
        string payload = string.Join('\n', fields);
        string expected = Hash(payload);
        string actual = EventEnsembleProtocol.AnalysisProtocolSha256;

        Assert.Matches("^[A-F0-9]{64}$", actual);
        Assert.Equal(actual, EventEnsembleProtocol.AnalysisProtocolSha256);
        Assert.Equal(expected, actual);
        foreach (int fieldIndex in Enumerable.Range(0, fields.Length))
        {
            string[] changedFields = (string[])fields.Clone();
            changedFields[fieldIndex] += "-changed";
            Assert.NotEqual(actual, Hash(string.Join('\n', changedFields)));
        }
    }

    private static string Hash(string payload) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
