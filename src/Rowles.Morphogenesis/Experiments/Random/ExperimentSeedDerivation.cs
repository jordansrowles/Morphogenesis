namespace Rowles.Morphogenesis.Experiments.Random;

/// <summary>
/// Derives domain-separated seeds with the SplitMix64 finaliser. Fixed domain tags identify initialisation and
/// dynamics; an improbable collision is resolved by toggling the low bit of the dynamics seed.
/// </summary>
public static class ExperimentSeedDerivation
{
    public const string SchemeVersion = "splitmix64-domain-v1";

    private const ulong InitialisationDomain = 0x494E495449414C31UL; // INITIAL1
    private const ulong DynamicsDomain = 0x44594E414D494331UL; // DYNAMIC1

    public static ulong DeriveInitialisation(ulong replicateSeed) => Derive(replicateSeed, InitialisationDomain);

    public static ulong DeriveDynamics(ulong replicateSeed)
    {
        ulong initialisationSeed = DeriveInitialisation(replicateSeed);
        ulong dynamicsSeed = Derive(replicateSeed, DynamicsDomain);
        return dynamicsSeed == initialisationSeed ? dynamicsSeed ^ 1UL : dynamicsSeed;
    }

    private static ulong Derive(ulong replicateSeed, ulong domain) =>
        ReplicateSeedDerivation.Mix(replicateSeed ^ domain);
}
