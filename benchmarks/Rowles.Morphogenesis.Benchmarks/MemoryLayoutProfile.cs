using System.Globalization;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Dynamics.Acceleration;
using Rowles.Morphogenesis.Initialisation;
using Rowles.Morphogenesis.Model;

namespace Rowles.Morphogenesis.Benchmarks;

internal static class MemoryLayoutProfile
{
    internal static void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using StreamWriter writer = new(path);
        writer.WriteLine("scenario,sites,cell_capacity,aos_array_bytes,soa_array_bytes,soa_hot_payload_bytes,soa_alive_payload_bytes,soa_array_header_count,halo_extra_payload_bytes,duplicated_halo_payload_bytes,kernel_plan_bytes,interface_index_bytes,border_members,directed_members");
        foreach (M3BenchmarkScenario scenario in M3BenchmarkScenario.All)
        {
            var manifest = scenario.Manifest;
            MorphogenesisState state = PackedAggregateInitialiser.Create(manifest, manifest.BaseSeed).State;
            _ = new KernelPlan(state.Configuration, state.ContactEnergies);
            _ = new InterfaceIndex(state);
            int capacity = state.Cells.Length;
            long before = GC.GetAllocatedBytesForCurrentThread();
            CellRuntime[] aos = new CellRuntime[capacity];
            long aosBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(aos);
            before = GC.GetAllocatedBytesForCurrentThread();
            int[] types = new int[capacity];
            int[] areas = new int[capacity];
            int[] perimeters = new int[capacity];
            double[] targetAreas = new double[capacity];
            double[] targetPerimeters = new double[capacity];
            double[] areaStiffnesses = new double[capacity];
            double[] perimeterStiffnesses = new double[capacity];
            bool[] alive = new bool[capacity];
            long soaBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(types); GC.KeepAlive(areas); GC.KeepAlive(perimeters);
            GC.KeepAlive(targetAreas); GC.KeepAlive(targetPerimeters);
            GC.KeepAlive(areaStiffnesses); GC.KeepAlive(perimeterStiffnesses); GC.KeepAlive(alive);
            before = GC.GetAllocatedBytesForCurrentThread();
            KernelPlan plan = new(state.Configuration, state.ContactEnergies);
            long planBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(plan);
            before = GC.GetAllocatedBytesForCurrentThread();
            InterfaceIndex index = new(state);
            long indexBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            long haloSites = (long)(state.Width + 2) * (state.Height + 2);
            writer.WriteLine(string.Join(',', scenario.Id, state.SiteCount, capacity, aosBytes, soaBytes,
                44L * capacity, capacity, 8, (haloSites - state.SiteCount) * sizeof(int), haloSites * sizeof(int),
                planBytes.ToString(CultureInfo.InvariantCulture), indexBytes.ToString(CultureInfo.InvariantCulture),
                index.BorderSites.Count, index.DirectedProposals.Count));
            GC.KeepAlive(index);
        }
    }
}
