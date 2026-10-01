using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Model;

namespace Rowles.Morphogenesis.Reference.Topology;

public readonly record struct ProposedConnectivityCheck(int LosingCellId, int? ComponentsAfter)
{
    public bool WouldRemainConnected => LosingCellId == 0 || ComponentsAfter == 1;
}
