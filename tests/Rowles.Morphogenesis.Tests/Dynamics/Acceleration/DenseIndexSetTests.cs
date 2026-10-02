using Rowles.Morphogenesis.Dynamics.Acceleration;
using Rowles.Morphogenesis.Random;

namespace Rowles.Morphogenesis.Tests.Dynamics.Acceleration;

public sealed class DenseIndexSetTests
{
    [Fact]
    public void Random_mutations_match_an_independent_set_and_dense_slots()
    {
        DenseIndexSet actual = new(97);
        HashSet<int> expected = [];
        System.Random random = new(17);
        for (int step = 0; step < 10000; step++)
        {
            int value = random.Next(97);
            if (random.Next(2) == 0) Assert.Equal(expected.Add(value), actual.Add(value));
            else Assert.Equal(expected.Remove(value), actual.Remove(value));
            Assert.Equal(expected.Count, actual.Count);
            for (int member = 0; member < 97; member++) Assert.Equal(expected.Contains(member), actual.Contains(member));
            Assert.Equal(expected.Order(), Enumerable.Range(0, actual.Count).Select(actual.At).Order());
        }
    }

    [Fact]
    public void Uniform_dense_slot_selection_reaches_each_member_exactly_once()
    {
        DenseIndexSet set = new(10);
        set.Add(8); set.Add(2); set.Add(5); set.Remove(2); set.Add(7);
        for (int position = 0; position < set.Count; position++)
            Assert.Equal(set.At(position), set.Select(new SlotRandom(position, set.Count)));
        Assert.Throws<ArgumentOutOfRangeException>(() => set.Add(10));
        Assert.Throws<ArgumentOutOfRangeException>(() => set.Remove(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => set.At(set.Count));
        Assert.Throws<InvalidOperationException>(() => new DenseIndexSet(0).Select(new SlotRandom(0, 0)));
    }

    private sealed class SlotRandom(int position, int expectedBound) : IRandomSource
    {
        public int NextInt(int exclusiveUpperBound) { Assert.Equal(expectedBound, exclusiveUpperBound); return position; }
        public double NextDouble() => throw new InvalidOperationException();
    }
}
