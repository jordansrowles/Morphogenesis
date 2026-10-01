using Rowles.Evolution.Algorithms;
using Rowles.Evolution.Archives;
using Rowles.Evolution.Evaluation;
using Rowles.Evolution.Random;
using Rowles.Evolution.Variation;

namespace Rowles.Evolution.Tests.Variation;

public sealed class IsoLineVariationTests
{
    [Fact]
    public void ReflectionHandlesVeryLargeFiniteOvershootAndKeepsExactBounds()
    {
        NumericBounds bound = new(-2, 3);
        Assert.InRange(IsoLineVariation.Reflect(1e300, bound), -2, 3);
        Assert.InRange(IsoLineVariation.Reflect(-1e300, bound), -2, 3);
        Assert.Equal(3, IsoLineVariation.Reflect(3, bound));
        Assert.Equal(3, IsoLineVariation.Reflect(-7, bound));
    }

    [Fact]
    public void ZeroCoefficientsReturnTheFirstParentAndSeededRunsMatch()
    {
        GridArchive parents = new(new GridArchiveConfiguration([0], [1], [2], ObjectiveDirection.Maximise));
        parents.TryInsert(0, [0.25], 1, [0.25], 0, out _);
        parents.TryInsert(1, [0.75], 1, [0.75], 0, out _);
        GridElite first = parents.GetAt(0)!;
        GridElite second = parents.GetAt(1)!;
        IsoLineVariation fixedVariation = new([new NumericBounds(0, 1)], sigmaIso: 0, sigmaLine: 0);
        Assert.Equal(new[] { 0.25 }, fixedVariation.Mutate(first, second, new EvolutionRandom(1)));

        GridMapElites firstRun = Create(2026);
        GridMapElites secondRun = Create(2026);
        IReadOnlyList<NumericCandidate> firstBatch = firstRun.Ask(64);
        IReadOnlyList<NumericCandidate> secondBatch = secondRun.Ask(64);
        Assert.Equal(firstBatch.Select(candidate => candidate.Values.ToArray()),
            secondBatch.Select(candidate => candidate.Values.ToArray()));
        Assert.All(firstBatch.SelectMany(candidate => candidate.Values), value => Assert.InRange(value, 0, 1));
    }

    private static GridMapElites Create(ulong seed) => new(new GridMapElitesConfiguration(
        [new NumericBounds(0, 1)], new GridArchiveConfiguration([0], [1], [4], ObjectiveDirection.Maximise)), seed);
}
