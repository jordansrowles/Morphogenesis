using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Tests.Dynamics;

public sealed class KernelPlanTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Resolved_plan_preserves_stencil_order_contact_values_and_enabled_rules(bool wall, bool moore)
    {
        SimulationConfiguration configuration = new(wall ? BoundaryMode.Wall : BoundaryMode.Periodic,
            LatticeConventions.Canonical with
            {
                ContactCouplingNeighbourhood = moore ? ContactCouplingNeighbourhood.Moore : ContactCouplingNeighbourhood.VonNeumann,
                PerimeterNeighbourhood = moore ? PerimeterNeighbourhood.Moore : PerimeterNeighbourhood.VonNeumann
            });
        ContactEnergyMatrix matrix = new(new double[,] { { 0, 7 }, { 7, 3 } });
        KernelPlan plan = new(configuration, matrix);
        Assert.Equal(new double[] { 0, 7, 7, 3 }, plan.ContactValues.ToArray());
        Assert.Equal(2, plan.ContactTypeCount);
        Assert.Equal(moore ? 8 : 4, plan.ContactOffsets.Length);
        Assert.Equal(moore ? 8 : 4, plan.PerimeterOffsets.Length);
        Assert.Equal(BuiltInEnergyTerms.Contact | BuiltInEnergyTerms.Area | BuiltInEnergyTerms.Perimeter, plan.EnergyTerms);
        Assert.Equal(BuiltInHardConstraints.FinalSite | BuiltInHardConstraints.Connectivity |
            (wall ? BuiltInHardConstraints.FixedWall : 0), plan.HardConstraints);
        for (int index = 0; index < plan.ContactOffsets.Length; index++)
        {
            StencilGeometry.ContactOffset(configuration.Conventions.ContactCouplingNeighbourhood, index, out int dx, out int dy);
            Assert.Equal(new NeighbourOffset(dx, dy), plan.ContactOffsets[index]);
            StencilGeometry.PerimeterOffset(configuration.Conventions.PerimeterNeighbourhood, index, out dx, out dy);
            Assert.Equal(new NeighbourOffset(dx, dy), plan.PerimeterOffsets[index]);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => matrix[-1, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => matrix[0, 2]);
    }
}
