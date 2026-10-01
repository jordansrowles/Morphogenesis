namespace Rowles.Evolution.Random;

/// <summary>Complete xoshiro state, including a cached Gaussian variate.</summary>
public sealed record RandomState(ulong S0, ulong S1, ulong S2, ulong S3, bool HasGaussian, double Gaussian);
