using Rowles.Morphogenesis.Reference.Tests.Fixtures;

namespace Rowles.Morphogenesis.Reference.Tests.Random;

public sealed class ScriptedRandomTests
{
    [Fact]
    public void Scripted_source_returns_exact_values_and_tracks_draw_counts()
    {
        ScriptedRandomSource random = new([4, 2], [0.75]);

        Assert.Equal(4, random.NextInt(25));
        Assert.Equal(2, random.NextInt(4));
        Assert.Equal(0.75, random.NextDouble());
        Assert.Equal(2, random.IntegerDrawCount);
        Assert.Equal(1, random.AcceptanceDrawCount);
        Assert.Equal([25, 4], random.IntegerUpperBounds);
        random.AssertExhausted();
    }

    [Fact]
    public void Scripted_source_fails_clearly_when_a_tape_is_exhausted()
    {
        ScriptedRandomSource random = new([], []);

        Assert.Contains("integer tape is exhausted", Assert.Throws<InvalidOperationException>(() => random.NextInt(10)).Message);
        Assert.Contains("acceptance tape is exhausted", Assert.Throws<InvalidOperationException>(() => random.NextDouble()).Message);
    }
}
