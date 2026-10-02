using Rowles.Morphogenesis.Random;

namespace Rowles.Morphogenesis.Dynamics.Acceleration;

/// <summary>A bounded integer set with constant-time mutation and uniform member selection.</summary>
internal sealed class DenseIndexSet
{
    private readonly int[] _dense;
    private readonly int[] _positions;

    internal DenseIndexSet(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _dense = new int[capacity];
        _positions = new int[capacity];
        Array.Fill(_positions, -1);
    }

    internal int Count { get; private set; }

    internal bool Contains(int value)
    {
        Validate(value);
        return _positions[value] >= 0;
    }

    internal bool Add(int value)
    {
        Validate(value);
        if (_positions[value] >= 0) return false;
        _positions[value] = Count;
        _dense[Count++] = value;
        return true;
    }

    internal bool Remove(int value)
    {
        Validate(value);
        int position = _positions[value];
        if (position < 0) return false;
        int last = _dense[--Count];
        _dense[position] = last;
        _positions[last] = position;
        _positions[value] = -1;
        return true;
    }

    internal int At(int position)
    {
        if ((uint)position >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(position));
        return _dense[position];
    }

    internal int Select(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (Count == 0) throw new InvalidOperationException("Cannot sample an empty set.");
        int position = random.NextInt(Count);
        if ((uint)position >= (uint)Count) throw new InvalidOperationException("The random source returned an invalid set index.");
        return _dense[position];
    }

    private void Validate(int value)
    {
        if ((uint)value >= (uint)_positions.Length) throw new ArgumentOutOfRangeException(nameof(value));
    }
}
