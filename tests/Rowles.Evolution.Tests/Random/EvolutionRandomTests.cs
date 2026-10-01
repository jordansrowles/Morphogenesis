using Rowles.Evolution.Random;

namespace Rowles.Evolution.Tests.Random;

public sealed class EvolutionRandomTests
{
    [Fact]
    public void GaussianSequenceHasStableBinary64Results()
    {
        EvolutionRandom random = new(0x0123456789abcdefUL);
        string actual = string.Join(",", Enumerable.Range(0, 12)
            .Select(_ => unchecked((ulong)BitConverter.DoubleToInt64Bits(random.NextGaussian())).ToString("X16")));

        Assert.Equal("3FEE20A3E40528C7,3FC1D49AABA439E1,3FF13A66E8302863,3FEBC6FBCA7470CD,3FB63E2400665734,3FE091BD385004D0,BFEA83B30978C7C8,BF772979C78E39E9,BFD126C72A64C89C,BFB64440230B6457,3FD1D6F56239F792,BFAA604879AB236B", actual);
    }

    [Fact]
    public void CachedGaussianCheckpointResumesTheExactSequence()
    {
        EvolutionRandom uninterrupted = new(0x0123456789abcdefUL);
        _ = uninterrupted.NextGaussian();
        RandomState state = uninterrupted.Capture();
        Assert.True(state.HasGaussian);

        EvolutionRandom resumed = new(state);
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(uninterrupted.NextGaussian()),
                BitConverter.DoubleToInt64Bits(resumed.NextGaussian()));
        }
    }
}
