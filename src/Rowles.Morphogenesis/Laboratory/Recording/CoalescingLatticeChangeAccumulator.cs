using Rowles.Morphogenesis.Dynamics;

namespace Rowles.Morphogenesis.Laboratory.Recording;

public sealed class CoalescingLatticeChangeAccumulator : ILatticeMutationSink
{
    private readonly ulong[] _changedSiteWords;
    private readonly int[] _changedIndices;
    private readonly int[] _changedCellIds;
    private int _count;
    private bool _indicesSorted = true;

    public CoalescingLatticeChangeAccumulator(int siteCount)
    {
        if (siteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(siteCount));
        }

        _changedSiteWords = new ulong[checked((siteCount + 63) / 64)];
        _changedIndices = new int[siteCount];
        _changedCellIds = new int[siteCount];
    }

    public int SiteCount => _changedIndices.Length;

    public int Count => _count;

    public ReadOnlySpan<int> SortedIndices
    {
        get
        {
            EnsureIndicesSorted();
            return _changedIndices.AsSpan(0, _count);
        }
    }

    public ReadOnlySpan<int> SortedCellIds => _changedCellIds.AsSpan(0, _count);

    public void AcceptedCopy(int targetIndex, int oldCellId, int newCellId)
    {
        if ((uint)targetIndex >= (uint)SiteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(targetIndex));
        }

        if (newCellId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newCellId));
        }

        int wordIndex = targetIndex >> 6;
        ulong siteMask = 1UL << (targetIndex & 63);
        ulong changedSites = _changedSiteWords[wordIndex];
        if ((changedSites & siteMask) == 0)
        {
            _changedSiteWords[wordIndex] = changedSites | siteMask;
            _changedIndices[_count++] = targetIndex;
            _indicesSorted = false;
        }
    }

    public void SortChanges(ReadOnlySpan<int> cellIdsBySite)
    {
        if (cellIdsBySite.Length != SiteCount)
        {
            throw new ArgumentException("Cell ID data must match the accumulator site count.", nameof(cellIdsBySite));
        }

        EnsureIndicesSorted();
        for (int position = 0; position < _count; position++)
        {
            _changedCellIds[position] = cellIdsBySite[_changedIndices[position]];
        }
    }

    public void Reset()
    {
        Array.Clear(_changedSiteWords);
        _count = 0;
        _indicesSorted = true;
    }

    private void EnsureIndicesSorted()
    {
        if (_indicesSorted || _count < 2)
        {
            _indicesSorted = true;
            return;
        }

        Array.Sort(_changedIndices, 0, _count);
        _indicesSorted = true;
    }
}
