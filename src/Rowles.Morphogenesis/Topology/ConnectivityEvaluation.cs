using System.Numerics;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Topology;

public readonly record struct ConnectivityEvaluation(bool RemainsConnected, bool UsedGlobalFallback, int RemainingSites);
