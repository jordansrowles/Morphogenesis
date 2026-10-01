using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using BenchmarkDotNet.Running;
using Rowles.Morphogenesis.Benchmarks;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Execution;
using Rowles.Morphogenesis.Initialisation;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Experiments.Results;

if (args.Length == 2 && args[0] == "--write-environment")
{
    M3BenchmarkEnvironment.Write(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "--profile-corpus")
{
    M3BenchmarkEnvironment.WriteKernelProfile(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "--profile-replicates")
{
    M3BenchmarkEnvironment.WriteReplicateProfile(args[1]);
    return;
}

M3BenchmarkEnvironment.ConfigureBenchmarkBuild();
BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);

internal static class M3BenchmarkEnvironment
{
    private static readonly IReadOnlyDictionary<string, string> BdnBuildSettings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
            ["MSBUILDUSESERVER"] = "0",
            ["BuildInParallel"] = "false",
            ["NuGetAudit"] = "false"
        };

    public static void ConfigureBenchmarkBuild()
    {
        foreach ((string name, string value) in BdnBuildSettings)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public static void Write(string path)
    {
        EnvironmentRecord record = new(
            DateTimeOffset.UtcNow,
            Git("rev-parse", "HEAD"),
            Git("status", "--porcelain=v1"),
            Git("diff", "--name-only", "HEAD", "--", "src/Rowles.Morphogenesis"),
            Run("dotnet", "--version"),
            typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
            Environment.Version.ToString(),
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            CpuModel(),
            PhysicalCoreCount(),
            TotalMemoryBytes(),
            GCSettings.IsServerGC,
            GCSettings.LatencyMode.ToString(),
            Stopwatch.Frequency,
            new Dictionary<string, string?>
            {
                ["COMPlus_TieredCompilation"] = System.Environment.GetEnvironmentVariable("COMPlus_TieredCompilation"),
                ["COMPlus_TieredPGO"] = System.Environment.GetEnvironmentVariable("COMPlus_TieredPGO"),
                ["DOTNET_TieredCompilation"] = System.Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                ["DOTNET_TieredPGO"] = System.Environment.GetEnvironmentVariable("DOTNET_TieredPGO")
            },
            BdnBuildSettings,
            "JIT; NativeAOT not used");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void WriteKernelProfile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using StreamWriter writer = new(path);
        writer.WriteLine("scenario_id,width,height,cells,boundary,kernel_mcs,attempts,accepted,rejected,no_op,fixed_wall,final_site,disconnected,metropolis,connectivity_fallbacks,elapsed_ms,attempts_per_second,mcs_per_second");
        foreach (M3BenchmarkScenario scenario in M3BenchmarkScenario.All)
        {
            PackedAggregateInitialisation initialisation = PackedAggregateInitialiser.Create(
                scenario.Manifest, scenario.Manifest.BaseSeed);
            SerialSimulation simulation = new(initialisation.State, scenario.Manifest.BaseSeed,
                scenario.Manifest.FluctuationAmplitude);
            long accepted = 0;
            long rejected = 0;
            long noOps = 0;
            long fixedWall = 0;
            long finalSite = 0;
            long disconnected = 0;
            long metropolis = 0;
            Stopwatch timer = Stopwatch.StartNew();
            long attempts = (long)scenario.KernelMcsPerInvocation * simulation.State.SiteCount;
            for (long index = 0; index < attempts; index++)
            {
                AttemptResult result = simulation.Attempt();
                switch (result.Status)
                {
                    case AttemptStatus.Accepted:
                        accepted++;
                        break;
                    case AttemptStatus.NoOp:
                        noOps++;
                        break;
                    case AttemptStatus.Rejected:
                        rejected++;
                        switch (result.RejectionReason)
                        {
                            case RejectionReason.FixedWall: fixedWall++; break;
                            case RejectionReason.FinalSite: finalSite++; break;
                            case RejectionReason.Disconnected: disconnected++; break;
                            case RejectionReason.Metropolis: metropolis++; break;
                        }
                        break;
                }
            }
            timer.Stop();
            double seconds = timer.Elapsed.TotalSeconds;
            writer.WriteLine(string.Join(',',
                scenario.Id,
                scenario.Manifest.GridWidth,
                scenario.Manifest.GridHeight,
                scenario.Manifest.Initialiser.CellCount,
                scenario.Manifest.BoundaryMode,
                scenario.KernelMcsPerInvocation,
                attempts,
                accepted,
                rejected,
                noOps,
                fixedWall,
                finalSite,
                disconnected,
                metropolis,
                simulation.ConnectivityFallbackCount,
                timer.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                (attempts / seconds).ToString("F1", CultureInfo.InvariantCulture),
                (scenario.KernelMcsPerInvocation / seconds).ToString("F2", CultureInfo.InvariantCulture)));
        }
    }

    public static void WriteReplicateProfile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using StreamWriter writer = new(path);
        writer.WriteLine("scenario_id,warmup_replicates,status,attempts,accepted,rejected,no_op,connectivity_fallbacks,elapsed_ms,measurement_ms,non_measurement_ms,allocated_bytes,sample_count,snapshot_count");
        foreach (M3BenchmarkScenario scenario in M3BenchmarkScenario.All)
        {
            _ = ExperimentRunner.RunReplicate(scenario.Manifest, 0);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            ExperimentReplicateResult result = ExperimentRunner.RunReplicate(scenario.Manifest, 0);
            writer.WriteLine(string.Join(',',
                scenario.Id,
                1,
                result.Status,
                result.AttemptCount,
                result.AcceptedAttemptCount,
                result.RejectedAttemptCount,
                result.NoOpAttemptCount,
                result.ConnectivityFallbackCount,
                result.ElapsedMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                result.MeasurementMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                (result.ElapsedMilliseconds - result.MeasurementMilliseconds).ToString("F3", CultureInfo.InvariantCulture),
                result.AllocatedBytes,
                result.Samples.Length,
                result.Snapshots.Length));
        }
    }

    private static string Git(params string[] arguments)
    {
        try
        {
            return Run("git", arguments);
        }
        catch (InvalidOperationException)
        {
            return "unavailable";
        }
    }

    private static string Run(string command, params string[] arguments)
    {
        ProcessStartInfo startInfo = new(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {command}.");
        string output = process.StandardOutput.ReadToEnd().Trim();
        string error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{command} exited with {process.ExitCode}: {error}");
        }

        return output;
    }

    private static string CpuModel()
    {
        if (!File.Exists("/proc/cpuinfo")) return "unavailable";
        return File.ReadLines("/proc/cpuinfo")
            .FirstOrDefault(line => line.StartsWith("model name", StringComparison.Ordinal))?
            .Split(':', 2).Last().Trim() ?? "unavailable";
    }

    private static int PhysicalCoreCount()
    {
        try
        {
            string output = Run("lscpu", "-p=socket,core");
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line[0] != '#')
                .Select(line => line.Trim())
                .Distinct(StringComparer.Ordinal)
                .Count();
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private static long TotalMemoryBytes()
    {
        if (!File.Exists("/proc/meminfo")) return 0;
        string? line = File.ReadLines("/proc/meminfo").FirstOrDefault(value => value.StartsWith("MemTotal:", StringComparison.Ordinal));
        return line is null ? 0 : long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture) * 1024;
    }

    private sealed record EnvironmentRecord(
        DateTimeOffset CapturedAtUtc,
        string Commit,
        string GitStatusPorcelain,
        string ProductionSourceDiff,
        string SdkVersion,
        string BenchmarkAssemblyVersion,
        string RuntimeVersion,
        string FrameworkDescription,
        string OsDescription,
        string OsArchitecture,
        string ProcessArchitecture,
        int LogicalProcessorCount,
        string CpuModel,
        int PhysicalCoreCount,
        long TotalMemoryBytes,
        bool ServerGc,
        string GcLatencyMode,
        long StopwatchFrequency,
        IReadOnlyDictionary<string, string?> TieredCompilationEnvironment,
        IReadOnlyDictionary<string, string> BenchmarkBuildEnvironment,
        string CompilationMode);
}
