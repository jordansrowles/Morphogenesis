using Rowles.Morphogenesis.Reference.Random;

namespace Rowles.Morphogenesis.Reference.Tests.Fixtures;

internal sealed class ScriptedRandomSource : IRandomSource
{
    private readonly Queue<int> _integers;
    private readonly Queue<double> _doubles;

    public ScriptedRandomSource(IEnumerable<int> integers, IEnumerable<double>? doubles = null)
    {
        _integers = new Queue<int>(integers);
        _doubles = new Queue<double>(doubles ?? []);
    }

    public int IntegerDrawCount { get; private set; }

    public int AcceptanceDrawCount { get; private set; }

    public List<int> IntegerUpperBounds { get; } = [];

    public int NextInt(int exclusiveUpperBound)
    {
        IntegerDrawCount++;
        IntegerUpperBounds.Add(exclusiveUpperBound);
        if (!_integers.TryDequeue(out int value))
        {
            throw new InvalidOperationException("The scripted integer tape is exhausted.");
        }

        if ((uint)value >= (uint)exclusiveUpperBound)
        {
            throw new InvalidOperationException($"Scripted integer {value} is outside [0, {exclusiveUpperBound}).");
        }

        return value;
    }

    public double NextDouble()
    {
        AcceptanceDrawCount++;
        if (!_doubles.TryDequeue(out double value))
        {
            throw new InvalidOperationException("The scripted acceptance tape is exhausted.");
        }

        return value;
    }

    public void AssertExhausted()
    {
        Assert.Empty(_integers);
        Assert.Empty(_doubles);
    }
}
