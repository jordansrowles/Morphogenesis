# Rowles.Morphogenesis

Rowles.Morphogenesis is a CPU-first .NET library for reproducible two-dimensional Cellular Potts Model simulations.

## Architecture

The repository contains two deliberately separate Cellular Potts implementations:

- `Rowles.Morphogenesis.Reference` is an independent mathematical reference implementation. It uses whole-state calculations, deterministic fixtures and topology diagnostics.
- `Rowles.Morphogenesis` is the production Cellular Potts implementation. It uses incremental geometry, hard connectivity constraints, explicit periodic and wall boundaries, and deterministic run replay.

The production implementation is checked against the reference implementation in tests. The reference project is not used in the production simulation path.

The repository also contains the M2.5 quality-diversity layer:

- `Rowles.Evolution` is a reusable deterministic .NET Grid MAP-Elites library with bounded numeric search, Iso+LineDD variation, archive metrics and checkpoint/resume support. It has no Morphogenesis dependency.
- `Rowles.Morphogenesis.Evolution` adapts Morphogenesis experiments to `Rowles.Evolution` without coupling the generic library back to the simulator.

The experiment framework resolves versioned manifests, initialises packed cell aggregates, runs independent replicates sequentially through the canonical serial kernel, and records measurements, snapshots and provenance. The headless runner and canonical experiment pack are documented in [`experiments/README.md`](experiments/README.md). Archived results and their scientific interpretation are documented in [`experiments/results/README.md`](experiments/results/README.md).

## Build and test

The repository uses one solution, `Rowles.Morphogenesis.slnx`, and pins the .NET 10 SDK in `global.json`.

```sh
dotnet restore Rowles.Morphogenesis.slnx
dotnet build Rowles.Morphogenesis.slnx --no-restore --configuration Release
dotnet test Rowles.Morphogenesis.slnx --no-build --no-restore --configuration Release
```

Run the deterministic synthetic baseline and bounded Morphogenesis QD proving campaign with:

```sh
dotnet run --project benchmarks/Rowles.Evolution.Benchmarks --configuration Release
```

Seeded serial runs are intended to be reproducible. Replay records capture the initial run inputs, seed, model configuration and executed attempt count so a run can be reconstructed from attempt zero.
