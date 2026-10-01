using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Random;
using Rowles.Morphogenesis.Topology;

namespace Rowles.Morphogenesis.Dynamics;

public readonly record struct McsSummary(
    int Attempts,
    int Accepted,
    int Rejected,
    int NoOps,
    int ConnectivityFallbacks);
