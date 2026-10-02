# Morphogenesis benchmark and qualification guide

Run commands from the repository root with .NET SDK 10.0.103. Restore the solution with:

```sh
dotnet restore Rowles.Morphogenesis.slnx -p:RestoreUseStaticGraphEvaluation=true -p:NuGetAudit=false
dotnet build Rowles.Morphogenesis.slnx --no-restore -c Release --disable-build-servers -m:1
```

The benchmark executable and all capture tools live in this project. Generated benchmark reports, captures and profiles belong under the ignored `benchmarks/results/` directory. Keep a separate, empty results directory for a new preregistered qualification; never overwrite the accepted capture pack.

## BenchmarkDotNet corpus

Run all benchmark classes or select a class with a BDN filter:

```sh
ARTIFACTS='benchmarks/results/m3-candidate-run'
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --job short --launchCount 3 --iterationCount 5 --warmupCount 10 --filter '*CanonicalKernelBenchmarks*' --artifacts "$ARTIFACTS" --exporters json --keepFiles --stopOnFirstError
```

Choose a new ignored artifact directory for each run.

Replace the filter with `*EndToEndReplicateBenchmarks*`, `*InitialisationBenchmarks*`, `*MeasurementBenchmarks*`, `*SnapshotBenchmarks*`, `*CanonicalPlanBenchmarks*`, `*CellLayoutBenchmarks*`, `*ContactLookupBenchmarks*`, `*LatticeLayoutBenchmarks*` or `*ProposalSpaceBenchmarks*` to select another mode. BDN raw CSV, full JSON and Markdown reports are generated under the selected artifacts directory. Keep those generated reports local; use concise reviewed summaries for durable evidence.

## Environment and fixed-seed profiles

These direct commands write CSV or JSON diagnostics, not BDN timings:

```sh
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --write-environment benchmarks/results/m3-candidate/environment.json
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --profile-corpus benchmarks/results/m3-candidate/kernel-profile.csv
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --profile-replicates benchmarks/results/m3-candidate/replicate-profile.csv
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --profile-stages benchmarks/results/m3-candidate/proposal-stage-profile.csv
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --profile-memory-layout benchmarks/results/m3-candidate/memory-layout.csv
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --profile-process-memory benchmarks/results/m3-candidate/process-memory.csv
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --profile-proposal-space benchmarks/results/m3-candidate/proposal-space-counters.csv
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --verify-layout-experiments
```

Stage timing copies perturb the hot path and are for attribution only. Process memory combines the runtime, harness, configuration and simulated state; it is not an exclusive state-size measurement. Proposal counters use all nine fixed benchmark scenarios.

## Event ensemble capture and qualification

Choose a new ignored output directory and point `PROTOCOL` to the frozen, external M3 ensemble protocol file at `../plan/Milestones/Work/M3/Sprint 1/M3 ensemble protocol frozen v1.md`. The accepted preregistration lives outside this repository; the versioned copy preserves the exact bytes whose SHA-256 is recorded in the qualification evidence.

The required sequence is:

1. Capture the canonical 64-seed ensemble.
2. Freeze the canonical equivalence bands.
3. Capture the border 64-seed ensemble.
4. Capture the directed 64-seed ensemble.
5. Qualify border against the frozen bands.
6. Qualify directed against the frozen bands.
7. Repeat the required paired captures and both qualifications at 128 and 256 seeds.
8. Confirm the configured system-size scenarios were included in those captures and qualifications. `sorting-64-t12` and `control-64-t12` run with every `--run-event-ensemble` command.

Use this sequence without inspecting candidate statistics before canonical bands are frozen:

```sh
RESULTS=benchmarks/results/m3-event-qualification
PROTOCOL='/path/to/frozen M3 ensemble protocol.md'

# 1. Capture the canonical 64-seed ensemble.
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --run-event-ensemble canonical "$RESULTS" 64

# 2. Freeze canonical equivalence bands and provenance.
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --freeze-event-bands "$RESULTS" "$PROTOCOL"

# 3. Capture both 64-seed alternative kernels.
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --run-event-ensemble border "$RESULTS" 64
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --run-event-ensemble directed "$RESULTS" 64

# 4. Qualify both against the frozen canonical bands.
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --compare-event-ensemble border "$RESULTS" 64
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --compare-event-ensemble directed "$RESULTS" 64

# 5. Expand all three paired captures to 128 seeds, then qualify both alternatives.
for KERNEL in canonical border directed; do
  dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --run-event-ensemble "$KERNEL" "$RESULTS" 128
done
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --compare-event-ensemble border "$RESULTS" 128
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --compare-event-ensemble directed "$RESULTS" 128

# 6. Expand all three paired captures to 256 seeds, then qualify both alternatives.
for KERNEL in canonical border directed; do
  dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --run-event-ensemble "$KERNEL" "$RESULTS" 256
done
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --compare-event-ensemble border "$RESULTS" 256
dotnet run -c Release --no-build --project benchmarks/Rowles.Morphogenesis.Benchmarks -- --compare-event-ensemble directed "$RESULTS" 256
```

Each ensemble contains sorting and control at 32 and 64 sites, three sorting fluctuation amplitudes, the wall-boundary sorting condition and the unchanged E01 high-fluctuation control. The 64-site cases preserve cell size and approximate occupancy; this is **system-size sensitivity**, not spatial-resolution refinement. Capture the configured 64-site system-size cases at each seed count as part of the same commands.

The capture command writes kernel-specific manifests named `<condition>-<kernel>-manifest-<seeds>.json`, sample CSV, per-cell CSV and deterministic initial-state hashes. The freeze command writes `canonical-bands-64.json` and a copy of the frozen protocol. Comparison writes a structured `<kernel>-summary-<seeds>.json` and detailed endpoint CSV. Raw sample, per-cell, manifest and comparison files are disposable local benchmark artefacts unless a project evidence record explicitly retains them. Preserve the frozen protocol, frozen-band JSON, provenance summary and qualification summaries with any retained local evidence. The full expensive ensemble is not part of ordinary CI.

Frozen-band format version 2 records both the integer analysis protocol version and a deterministic analysis protocol SHA-256. Canonical captures at 128 and 256 seeds are checked against the frozen 64-seed scientific configuration, excluding only the top-level `replicateCount`. Qualification summaries include the exact canonical, candidate, frozen-band and protocol identities used for each comparison.

## NativeAOT and runtime comparison

Restore the Linux x64 NativeAOT runtime pack with its publish properties, then publish without warning suppression:

```sh
dotnet restore benchmarks/Rowles.Morphogenesis.RuntimeProbe/Rowles.Morphogenesis.RuntimeProbe.csproj -r linux-x64 -p:PublishAot=true -p:SelfContained=true -p:RuntimeFrameworkVersion=10.0.3 -p:RestoreUseStaticGraphEvaluation=true -p:NuGetAudit=false
dotnet publish benchmarks/Rowles.Morphogenesis.RuntimeProbe/Rowles.Morphogenesis.RuntimeProbe.csproj -c Release -r linux-x64 --no-restore -p:PublishAot=true -p:SelfContained=true -p:RuntimeFrameworkVersion=10.0.3 --disable-build-servers -m:1
```

Run the warmed managed probe with tiered compilation enabled and dynamic PGO disabled, then enabled, and run the published NativeAOT executable:

```sh
DOTNET_TieredCompilation=1 DOTNET_TieredPGO=0 dotnet benchmarks/Rowles.Morphogenesis.RuntimeProbe/bin/Release/net10.0/Rowles.Morphogenesis.RuntimeProbe.dll tiered-jit 7
DOTNET_TieredCompilation=1 DOTNET_TieredPGO=1 dotnet benchmarks/Rowles.Morphogenesis.RuntimeProbe/bin/Release/net10.0/Rowles.Morphogenesis.RuntimeProbe.dll dynamic-pgo 7
benchmarks/Rowles.Morphogenesis.RuntimeProbe/bin/Release/net10.0/linux-x64/publish/Rowles.Morphogenesis.RuntimeProbe native-aot 7
```

Each probe writes one CSV header plus five scenarios. It warms each scenario for at least one second, constructs each measured state outside timing, and records counters, final-state hash, per-thread allocation and generation 0/1/2 collection deltas. Rotate process launch order across runtimes and store each process's output separately before calculating means, dispersion and ratios. Do not run concurrent workloads during performance captures.

## Qualification implementation

The C# statistical implementation and synthetic unit/workflow tests are under `Analysis/` and `tests/Rowles.Morphogenesis.Tests/Analysis/`. Run the focused tests with:

```sh
dotnet test tests/Rowles.Morphogenesis.Tests/Rowles.Morphogenesis.Tests.csproj -c Release --no-restore --disable-build-servers -m:1
```

Qualification failures return a nonzero process result and write structured failure counts/details; input integrity errors also return nonzero with a diagnostic. Never infer equivalence from a p-value or from a partial set of scenarios, metrics or checkpoints.
