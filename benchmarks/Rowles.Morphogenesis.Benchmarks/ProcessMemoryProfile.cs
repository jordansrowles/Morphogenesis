using System.Diagnostics;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Dynamics.Acceleration;
using Rowles.Morphogenesis.Initialisation;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Benchmarks;

/// <summary>Process observations and per-thread allocation; deliberately outside headline timing.</summary>
internal static class ProcessMemoryProfile
{
    internal static void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using StreamWriter writer = new(path);
        writer.WriteLine("scenario,kernel,warmup_mcs,observed_mcs,managed_heap_bytes,process_working_set_bytes,allocated_bytes,gen0_collections,gen1_collections,gen2_collections");
        foreach (M3BenchmarkScenario scenario in M3BenchmarkScenario.All)
        {
            foreach (string kernel in new[] { "canonical", "border", "directed" })
            {
                Capture(writer, scenario, kernel);
            }
        }
    }

    private static void Capture(StreamWriter writer, M3BenchmarkScenario scenario, string kernel)
    {
        var manifest = scenario.Manifest;
        MorphogenesisState state = PackedAggregateInitialiser.Create(manifest, manifest.BaseSeed).State;
        SerialSimulation? canonical = kernel == "canonical"
            ? new SerialSimulation(state, manifest.BaseSeed, manifest.FluctuationAmplitude) : null;
        EventClockSimulation? events = kernel == "canonical" ? null
            : new EventClockSimulation(state, manifest.BaseSeed, manifest.FluctuationAmplitude,
                kernel == "border" ? ProposalSpaceKind.BorderSites : ProposalSpaceKind.DirectedInterface);
        McsSummary Run() => canonical is not null ? canonical.RunMcs() : events!.RunMcs();
        Stopwatch timer = Stopwatch.StartNew();
        int warmupMcs = 0;
        do
        {
            Run();
            warmupMcs++;
        }
        while (timer.ElapsedMilliseconds < 1000);

        long heap = GC.GetTotalMemory(forceFullCollection: true);
        using Process process = Process.GetCurrentProcess();
        long workingSet = process.WorkingSet64;
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        long before = GC.GetAllocatedBytesForCurrentThread();
        const int observedMcs = 100;
        for (int index = 0; index < observedMcs; index++)
        {
            Run();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        int collections0 = GC.CollectionCount(0) - gen0;
        int collections1 = GC.CollectionCount(1) - gen1;
        int collections2 = GC.CollectionCount(2) - gen2;
        state.ValidateInvariants();
        writer.WriteLine(string.Join(',', scenario.Id, kernel, warmupMcs, observedMcs,
            heap, workingSet, allocated, collections0, collections1, collections2));
        GC.KeepAlive(canonical);
        GC.KeepAlive(events);
    }
}
