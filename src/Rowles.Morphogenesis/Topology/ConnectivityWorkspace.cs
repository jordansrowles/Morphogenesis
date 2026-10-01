using System.Numerics;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Topology;

public sealed class ConnectivityWorkspace
{
    internal readonly int[] Queue;
    internal readonly int[] VisitedAt;
    internal int Generation;

    public ConnectivityWorkspace(int siteCount)
    {
        if (siteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(siteCount));
        }

        Queue = new int[siteCount];
        VisitedAt = new int[siteCount];
    }
}
