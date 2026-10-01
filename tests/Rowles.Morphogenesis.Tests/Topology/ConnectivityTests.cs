using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Tests.Fixtures;
using Rowles.Morphogenesis.Topology;
using ReferenceConnectivity = Rowles.Morphogenesis.Reference.Topology.ConnectivityReference;
using ReferenceGridPoint = Rowles.Morphogenesis.Reference.Lattice.GridPoint;

namespace Rowles.Morphogenesis.Tests.Topology;

public sealed class ConnectivityTests
{
    private static readonly int[,] MooreOffsets =
    {
        { -1, -1 }, { 0, -1 }, { 1, -1 }, { -1, 0 },
        { 1, 0 }, { -1, 1 }, { 0, 1 }, { 1, 1 }
    };

    private static readonly double[,] Contacts = { { 0, 2 }, { 2, 1 } };

    [Fact]
    public void Local_and_global_removal_decisions_agree_for_every_valid_3_by_3_ring_pattern()
    {
        int checkedPatterns = 0;
        int fallbackPatterns = 0;
        for (int mask = 0; mask < 1 << 8; mask++)
        {
            int[] ids = new int[25];
            int target = 2 * 5 + 2;
            ids[target] = 1;
            for (int bit = 0; bit < 8; bit++)
            {
                if ((mask & (1 << bit)) != 0)
                {
                    int x = 2 + MooreOffsets[bit, 0];
                    int y = 2 + MooreOffsets[bit, 1];
                    ids[y * 5 + x] = 1;
                }
            }

            Rowles.Morphogenesis.Reference.Model.ReferenceState reference = MakeReferenceState(ids, 5, 5);
            if (!ReferenceConnectivity.IsConnected(reference, 1))
            {
                continue;
            }

            MorphogenesisState production = new(5, 5, ids, [Cell()], new ContactEnergyMatrix(Contacts));
            Rowles.Morphogenesis.Reference.Model.ReferenceState after = reference.Clone();
            after.SetCellId(new ReferenceGridPoint(2, 2), 0);
            bool expected = ReferenceConnectivity.CountComponents(after, 1) == 1;

            ConnectivityEvaluation actual = CellConnectivity.EvaluateRemoval(production, target, new ConnectivityWorkspace(25));
            Assert.Equal(expected, actual.RemainsConnected);
            if (actual.UsedGlobalFallback)
            {
                fallbackPatterns++;
            }

            checkedPatterns++;
        }

        Assert.True(checkedPatterns > 0);
        Assert.True(fallbackPatterns > 0);
    }

    [Fact]
    public void Ambiguous_local_pattern_uses_global_fallback_to_allow_a_globally_connected_cell()
    {
        int[] ids = new int[49];
        Add(3, 3);
        Add(3, 4);
        Add(3, 5);
        Add(4, 5);
        Add(5, 5);
        Add(5, 4);
        Add(5, 3);
        Add(5, 2);
        Add(4, 2);
        Add(3, 2);
        MorphogenesisState state = new(7, 7, ids, [Cell()], new ContactEnergyMatrix(Contacts));

        ConnectivityEvaluation result = CellConnectivity.EvaluateRemoval(state, 3 * 7 + 3, new ConnectivityWorkspace(49));

        Assert.True(result.RemainsConnected);
        Assert.True(result.UsedGlobalFallback);

        void Add(int x, int y) => ids[y * 7 + x] = 1;
    }

    [Fact]
    public void Ambiguous_bridge_removal_uses_global_fallback_to_reject_fragmentation()
    {
        int[] ids = new int[49];
        ids[3 * 7 + 3] = 1;
        ids[4 * 7 + 3] = 1;
        ids[2 * 7 + 3] = 1;
        MorphogenesisState state = new(7, 7, ids, [Cell()], new ContactEnergyMatrix(Contacts));

        ConnectivityEvaluation result = CellConnectivity.EvaluateRemoval(state, 3 * 7 + 3, new ConnectivityWorkspace(49));

        Assert.False(result.RemainsConnected);
        Assert.True(result.UsedGlobalFallback);
    }

    [Fact]
    public void Final_site_is_not_considered_a_connected_removal()
    {
        int[] ids = new int[25];
        ids[12] = 1;
        MorphogenesisState state = new(5, 5, ids, [Cell()], new ContactEnergyMatrix(Contacts));

        ConnectivityEvaluation result = CellConnectivity.EvaluateRemoval(state, 12, new ConnectivityWorkspace(25));

        Assert.False(result.RemainsConnected);
        Assert.False(result.UsedGlobalFallback);
        Assert.Equal(0, result.RemainingSites);
    }

    [Fact]
    public void G08_biological_ring_is_valid_even_when_its_medium_complement_is_disconnected()
    {
        MorphogenesisState state = ProductionTestStateFactory.FromRows(
            [".......", ".......", "..AAA..", "..A.A..", "..AAA..", ".......", "......."],
            [new CellDefinition(1, 1, 8, 1, 32, 0.1)],
            Contacts);

        state.ValidateInvariants();

        Assert.Equal(8, state.GetCellState(1).Area);
        Assert.Equal(1, CellConnectivity.CountComponents(state, 1));
    }

    private static CellDefinition Cell() => new(1, 1, 4, 1, 8, 0.1);

    private static Rowles.Morphogenesis.Reference.Model.ReferenceState MakeReferenceState(int[] ids, int width, int height)
    {
        int[,] lattice = new int[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                lattice[x, y] = ids[y * width + x];
            }
        }

        return new Rowles.Morphogenesis.Reference.Model.ReferenceState(
            lattice,
            [new Rowles.Morphogenesis.Reference.Model.CellDefinition(1, 1, 4, 1, 8, 0.1)],
            [new Rowles.Morphogenesis.Reference.Model.CellTypeDefinition(0, "Medium"), new(1, "A")],
            new Rowles.Morphogenesis.Reference.Model.ContactEnergyMatrix(Contacts));
    }
}
