using System.Diagnostics;
using System.Globalization;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;

// This probe deliberately has no manifest deserialisation or BenchmarkDotNet dependency.
// The same public canonical kernel and fixed fixtures run under JIT and NativeAOT.
string label = args.Length > 0 ? args[0] : "unspecified";
int samples = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 7;
if (samples < 1) throw new ArgumentOutOfRangeException(nameof(samples));
Console.WriteLine("runtime,scenario,sample,mcs,elapsed_ms,allocated_bytes,gc_gen0,gc_gen1,gc_gen2,accepted,rejected,no_ops,fallbacks,state_hash");
foreach (ProbeFixture fixture in ProbeFixture.All)
{
    SerialSimulation warmup = fixture.Create();
    Stopwatch warmupTimer = Stopwatch.StartNew();
    do { warmup.RunMcs(); } while (warmupTimer.ElapsedMilliseconds < 1000);
    for (int sample = 0; sample < samples; sample++)
    {
        SerialSimulation simulation = fixture.Create();
        long accepted = 0, rejected = 0, noOps = 0, fallbacks = 0;
        Stopwatch timer = new();
        long before = GC.GetAllocatedBytesForCurrentThread();
        int generationZeroBefore = GC.CollectionCount(0);
        int generationOneBefore = GC.CollectionCount(1);
        int generationTwoBefore = GC.CollectionCount(2);
        timer.Start();
        for (int mcs = 0; mcs < fixture.BatchMcs; mcs++)
        {
            McsSummary summary = simulation.RunMcs();
            accepted += summary.Accepted; rejected += summary.Rejected;
            noOps += summary.NoOps; fallbacks += summary.ConnectivityFallbacks;
        }
        timer.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        int generationZeroCollections = GC.CollectionCount(0) - generationZeroBefore;
        int generationOneCollections = GC.CollectionCount(1) - generationOneBefore;
        int generationTwoCollections = GC.CollectionCount(2) - generationTwoBefore;
        ulong hash = fixture.Hash(simulation.State);
        Console.WriteLine(string.Join(',', label, fixture.Id, sample, fixture.BatchMcs,
            timer.Elapsed.TotalMilliseconds.ToString("F6", CultureInfo.InvariantCulture),
            allocated, generationZeroCollections, generationOneCollections, generationTwoCollections,
            accepted, rejected, noOps, fallbacks, hash.ToString("X16", CultureInfo.InvariantCulture)));
    }
}

internal sealed record ProbeFixture(string Id, int Width, bool Wall, bool Sparse, int BatchMcs)
{
    internal static readonly ProbeFixture[] All =
    [
        new("sparse-20", 20, false, true, 4000),
        new("confluent-32", 32, false, false, 2000),
        new("confluent-128", 128, false, false, 150),
        new("confluent-256", 256, false, false, 40),
        new("wall-64", 64, true, false, 500)
    ];

    internal SerialSimulation Create()
    {
        int[] ids = new int[Width * Width];
        List<CellDefinition> definitions = [];
        if (Sparse)
        {
            for (int y = Width / 2 - 1; y <= Width / 2 + 1; y++)
                for (int x = Width / 2 - 1; x <= Width / 2 + 1; x++) ids[y * Width + x] = 1;
            definitions.Add(new CellDefinition(1, 1, 16, 2, 16, 0.5));
        }
        else
        {
            int across = Width / 4;
            for (int y = 0; y < Width; y++)
                for (int x = 0; x < Width; x++) ids[y * Width + x] = 1 + y / 4 * across + x / 4;
            for (int id = 1; id <= across * across; id++)
                definitions.Add(new CellDefinition(id, 1 + id % 2, 16, 2, 16, 0.5));
        }
        ContactEnergyMatrix contact = new(new double[,] { { 0, 12, 12 }, { 12, 4, 14 }, { 12, 14, 4 } });
        MorphogenesisState state = new(Width, Width, ids, definitions, contact,
            Wall ? SimulationConfiguration.WallCanonical : SimulationConfiguration.PeriodicCanonical);
        return new SerialSimulation(state, 1701UL, 12);
    }

    internal ulong Hash(MorphogenesisState state)
    {
        ulong hash = 14695981039346656037UL;
        static ulong Add(ulong current, ulong value) => unchecked((current ^ value) * 1099511628211UL);
        foreach (int id in state.GetCellIdsCopy()) hash = Add(hash, (ulong)id);
        int cellCount = Sparse ? 1 : Width / 4 * (Width / 4);
        for (int id = 1; id <= cellCount; id++)
        {
            CellState cell = state.GetCellState(id);
            hash = Add(hash, (ulong)cell.Area);
            hash = Add(hash, (ulong)cell.Perimeter);
        }
        return hash;
    }
}
