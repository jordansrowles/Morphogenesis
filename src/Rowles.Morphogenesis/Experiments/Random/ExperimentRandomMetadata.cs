using Rowles.Morphogenesis.Random;

namespace Rowles.Morphogenesis.Experiments.Random;

/// <summary>Identifies the pseudorandom algorithm and deterministic seed policy used by an experiment.</summary>
public sealed record ExperimentRandomMetadata(string Algorithm, string SeedDerivationScheme)
{
    public static ExperimentRandomMetadata Current { get; } =
        new(Xoshiro256StarStar.AlgorithmName, ExperimentSeedDerivation.SchemeVersion);

    public void Validate()
    {
        if (Algorithm != Xoshiro256StarStar.AlgorithmName || SeedDerivationScheme != ExperimentSeedDerivation.SchemeVersion)
        {
            throw new NotSupportedException("The experiment result uses an unsupported PRNG or seed derivation scheme.");
        }
    }
}
