# Rowles.Evolution

`Rowles.Evolution` is a separate .NET 10 library for deterministic numeric Grid MAP-Elites. Its generic archive, scheduler, evaluator contract, RNG, variation and checkpoint types have no Morphogenesis dependency.

The optional `Rowles.Morphogenesis.Evolution` integration project uses the sibling checkout at `../code`. Clone both repositories as siblings, restore `Rowles.Evolution.slnx`, then build it. The core library itself can be built independently.

The first implementation provides closed-bound N-dimensional grids, one elite per cell, deterministic ask/tell batches, Iso+LineDD mutation with reflection, explicit objective direction and baseline-defined QD score, and source-generated versioned JSON checkpoints. The integration project uses the headless M2 experiment runner and the fixed two-replicate E02-shaped campaign.

Run the deterministic unit and integration suite with:

```sh
dotnet test Rowles.Evolution.slnx
```

Run the 8-batch, 128-candidate proving campaign and synthetic throughput baseline with:

```sh
dotnet run --project benchmarks/Rowles.Evolution.Benchmarks
```
