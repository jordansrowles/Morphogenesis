using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Energy;

public readonly record struct HamiltonianBreakdown(double Contact, double Area, double Perimeter)
{
    public double Total => Contact + Area + Perimeter;
}
