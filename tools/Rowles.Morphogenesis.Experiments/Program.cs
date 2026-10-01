using System.Diagnostics;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Execution;
using Rowles.Morphogenesis.Experiments.Random;
using Rowles.Morphogenesis.Experiments.Results;

namespace Rowles.Morphogenesis.Experiments.Tool;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            return await ExecuteAsync(args).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Experiment command failed: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> ExecuteAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        if (args[0] != "run" || args.Length < 2)
        {
            PrintUsage();
            return 1;
        }

        string manifestPath = Path.GetFullPath(args[1]);
        string? outputPath = null;
        int? replicateOverride = null;
        // Parse the small fixed CLI grammar here; the replicate override is applied to a resolved copy below.
        for (int index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--output" when index + 1 < args.Length:
                    outputPath = Path.GetFullPath(args[++index]);
                    break;
                case "--replicates" when index + 1 < args.Length:
                    if (!int.TryParse(args[++index], out int count) || count <= 0)
                    {
                        throw new ArgumentException("--replicates requires a positive integer.");
                    }

                    replicateOverride = count;
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete option '{args[index]}'.");
            }
        }

        ExperimentManifest manifest = ExperimentManifest.ReadJson(manifestPath);
        if (replicateOverride is not null)
        {
            manifest = manifest with { ReplicateCount = replicateOverride.Value };
            manifest.Validate();
        }

        // Capture source and runtime provenance before executing so the result explains its own environment.
        string? commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? await RunCommandAsync("git", "rev-parse", "HEAD").ConfigureAwait(false);
        string? gitStatus = await RunCommandAsync("git", "status", "--porcelain").ConfigureAwait(false);
        string sourceTreeState = gitStatus is null ? "unknown" : gitStatus.Length == 0 ? "clean" : "dirty";
        string? sdkVersion = Environment.GetEnvironmentVariable("DOTNET_SDK_VERSION") ?? await RunCommandAsync("dotnet", "--version").ConfigureAwait(false);
        outputPath ??= Path.GetFullPath(Path.Combine("experiments", "results", $"{manifest.ExperimentId}.json"));

        Console.WriteLine($"Experiment: {manifest.ExperimentId} - {manifest.Name}");
        Console.WriteLine($"Schema: {manifest.SchemaVersion}; kernel: canonical-serial-v1; source commit: {commit ?? "unknown"} ({sourceTreeState} tree)");
        Console.WriteLine($"Runtime: {Environment.Version}; SDK: {sdkVersion ?? "unknown"}; OS: {Environment.OSVersion}; architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"Grid: {manifest.GridWidth}x{manifest.GridHeight}; boundary: {manifest.BoundaryMode}; copy: {manifest.CopyNeighbourhood}; contact: {manifest.ContactCouplingNeighbourhood}; perimeter: {manifest.PerimeterNeighbourhood}; connectivity: {manifest.ConnectivityAdjacency}");
        Console.WriteLine($"MCS: {manifest.McsCount}; cells: {manifest.Initialiser.CellCount}; target area: {manifest.Initialiser.ApproximateTargetCellArea}; fluctuation amplitude: {manifest.FluctuationAmplitude}; replicates: {manifest.ReplicateCount}");
        Console.WriteLine($"Contact matrix: {System.Text.Json.JsonSerializer.Serialize(manifest.ContactEnergies)}");
        Console.WriteLine($"Random: {ExperimentRandomMetadata.Current.Algorithm}; seed derivation: {ExperimentRandomMetadata.Current.SeedDerivationScheme}.");
        // Separate derived streams keep initial tissue placement independent from subsequent CPM dynamics draws.
        Console.WriteLine("Each replicate derives separate initialisation and dynamics seeds from its replicate seed.");

        int progressInterval = Math.Max(1, manifest.ReplicateCount / 10);
        ExperimentEnsembleResult result = ExperimentRunner.Run(
            manifest,
            softwareCommit: commit,
            progress: (completed, total) =>
            {
                if (completed % progressInterval == 0 || completed == total)
                {
                    Console.WriteLine($"Replicates: {completed}/{total}");
                }
            },
            sdkVersion: sdkVersion,
            sourceTreeState: sourceTreeState);

        // Persist the complete raw result first, then print concise scientific and throughput summaries.
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, result.ToJson());
        Console.WriteLine($"Replicate seeds: {string.Join(", ", result.ReplicateSeeds)}");
        Console.WriteLine($"Completed: {result.Summary.SuccessfulReplicates}/{result.Summary.AttemptedReplicates}; failed: {result.Summary.FailedReplicates}");
        PrintMetric("Final heterotypic interface fraction", result.Summary.FinalMetricStatistics["heterotypic-interface-fraction"]);
        PrintMetric("Accepted attempt fraction", result.Summary.FinalMetricStatistics["accepted-attempt-fraction"]);

        long attempts = result.Replicates.Sum(replicate => replicate.AttemptCount);
        long connectivityFallbacks = result.Replicates.Sum(replicate => replicate.ConnectivityFallbackCount);
        double measurementMilliseconds = result.Replicates.Sum(replicate => replicate.MeasurementMilliseconds);
        double seconds = result.ElapsedMilliseconds / 1000;
        double attemptsPerSecond = seconds <= 0 ? 0 : attempts / seconds;
        double mcsPerSecond = seconds <= 0 ? 0 : (double)manifest.McsCount * result.Summary.SuccessfulReplicates / seconds;
        Console.WriteLine($"Performance baseline: {attemptsPerSecond:F0} attempts/s; {mcsPerSecond:F2} MCS/s; {result.AllocatedBytes} allocated bytes; {measurementMilliseconds:F2} ms measured; {connectivityFallbacks} connectivity fallbacks.");
        Console.WriteLine($"Ensemble wall time: {result.ElapsedMilliseconds:F2} ms. Result file: {outputPath}");
        return result.Summary.FailedReplicates == 0 ? 0 : 2;
    }

    private static void PrintMetric(string name, MetricStatistics metric) =>
        Console.WriteLine($"{name} (n={metric.Count}): mean={metric.Mean:F4}, median={metric.Median:F4}, sample SD={metric.SampleStandardDeviation:F4}, min={metric.Minimum:F4}, max={metric.Maximum:F4}");

    private static async Task<string?> RunCommandAsync(string command, params string[] arguments)
    {
        using Process process = new();
        try
        {
            ProcessStartInfo startInfo = new(command)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            process.StartInfo = startInfo;
            if (!process.Start())
            {
                return null;
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Stop a timed-out child and its descendants, then wait again so no process is left running.
                KillProcessTree(process);
                using CancellationTokenSource killTimeout = new(TimeSpan.FromSeconds(5));
                try
                {
                    await process.WaitForExitAsync(killTimeout.Token).ConfigureAwait(false);
                    await outputTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (TimeoutException)
                {
                }

                return null;
            }

            string output = await outputTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception)
        {
            KillProcessTree(process);
            return null;
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: dotnet run --project tools/Rowles.Morphogenesis.Experiments -- run <manifest.json> [--output <result.json>] [--replicates <count>]");
    }
}
