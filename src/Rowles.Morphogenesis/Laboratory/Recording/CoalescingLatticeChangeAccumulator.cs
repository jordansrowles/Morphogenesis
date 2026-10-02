using Rowles.Morphogenesis.Dynamics;

namespace Rowles.Morphogenesis.Laboratory.Recording;

public sealed class CoalescingLatticeChangeAccumulator : ILatticeMutationSink
{
    private readonly int[] _positionBySite;
    private readonly int[] _changedIndices;
    private readonly int[] _changedCellIds;
    private int _count;
    private bool _isSorted = true;

    public CoalescingLatticeChangeAccumulator(int siteCount)
    {
        if (siteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(siteCount));
        }

        _positionBySite = new int[siteCount];
        Array.Fill(_positionBySite, -1);
        _changedIndices = new int[siteCount];
        _changedCellIds = new int[siteCount];
    }

    public int SiteCount => _positionBySite.Length;

    public int Count => _count;

    public ReadOnlySpan<int> SortedIndices
    {
        get
        {
            EnsureSorted();
            return _changedIndices.AsSpan(0, _count);
        }
    }

    public ReadOnlySpan<int> SortedCellIds
    {
        get
        {
            EnsureSorted();
            return _changedCellIds.AsSpan(0, _count);
        }
    }

    public void AcceptedCopy(int targetIndex, int oldCellId, int newCellId)
    {
        if ((uint)targetIndex >= (uint)_positionBySite.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(targetIndex));
        }

        if (newCellId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newCellId));
        }

        int position = _positionBySite[targetIndex];
        if (position < 0)
        {
            position = _count++;
            _positionBySite[targetIndex] = position;
            _changedIndices[position] = targetIndex;
        }

        _changedCellIds[position] = newCellId;
        _isSorted = false;
    }

    public void SortChanges()
    {
        EnsureSorted();
    }

    public void Reset()
    {
        for (int position = 0; position < _count; position++)
        {
            _positionBySite[_changedIndices[position]] = -1;
        }

        _count = 0;
        _isSorted = true;
    }

    private void EnsureSorted()
    {
        if (_isSorted || _count < 2)
        {
            _isSorted = true;
            return;
        }

        for (int root = _count / 2 - 1; root >= 0; root--)
        {
            SiftDown(root, _count);
        }

        for (int end = _count - 1; end > 0; end--)
        {
            Swap(0, end);
            SiftDown(0, end);
        }

        _isSorted = true;
    }

    private void SiftDown(int root, int length)
    {
        while (true)
        {
            int child = checked(root * 2 + 1);
            if (child >= length)
            {
                return;
            }

            if (child + 1 < length && _changedIndices[child] < _changedIndices[child + 1])
            {
                child++;
            }

            if (_changedIndices[root] >= _changedIndices[child])
            {
                return;
            }

            Swap(root, child);
            root = child;
        }
    }

    private void Swap(int first, int second)
    {
        (_changedIndices[first], _changedIndices[second]) = (_changedIndices[second], _changedIndices[first]);
        (_changedCellIds[first], _changedCellIds[second]) = (_changedCellIds[second], _changedCellIds[first]);
    }
}
