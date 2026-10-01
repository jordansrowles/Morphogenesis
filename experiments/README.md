# Canonical experiment pack

These schema-v1 JSON manifests run through the production `SerialSimulation` kernel. Each scientific value that affects the simulation or measurement is explicit. Result JSON preserves the resolved manifest, random-stream provenance, raw replicate series, failures, snapshots when enabled, and the complete aggregate summary.

## Cases

| Case | Purpose | Main setup |
| --- | --- | --- |
| E00 | Single-cell mechanics smoke test through the experiment runner | One type-A cell, 20×20 periodic lattice, 40 MCS, one replicate |
| E01 low/middle/high | Fluctuation-amplitude sweep with common replicate seeds and initial tissues | 16 cells, 32×32 lattice, 40 MCS, eight replicates per amplitude |
| E02 control/sorting | Main two-type differential-adhesion comparison | 16 cells, 32×32 lattice, 80 MCS, 16 replicates per regime |
| E02 population perturbation | Check that the regime ordering survives a 25% increase in cell count | 20 cells, 36×36 lattice, 80 MCS, 16 replicates per regime |

E02 conditions share the same base seed, initialiser, cell mechanics, fluctuation amplitude, measurement schedule, and replicate count. Each matched replicate index therefore has the same initial occupancy and A/B assignment, plus the same dynamics seed. The control contact matrix has `J(A,A)=J(B,B)=J(A,B)=2`; the sorting matrix changes only `J(A,B)` to 20. Both use `J(medium,A)=J(medium,B)=10` and `J(medium,medium)=0`. The intended effective type-interface energy is therefore higher in the sorting condition. Acceptance depends on replicate distributions and supporting contact measurements.

## Random streams and provenance

The PRNG is `xoshiro256** with SplitMix64`. The `splitmix64-domain-v1` seed policy first derives a replicate seed from the manifest base seed and zero-based replicate index. It derives separate initialisation and dynamics seeds from that replicate seed using fixed domain tags and the SplitMix64 finaliser. If the two domain outputs ever collide, the dynamics seed's low bit is toggled. The derivation code and scheme identifier are in each result JSON.

Per replicate, results record `seed`, `initialisationSeed`, and `dynamicsSeed`. The aggregate `random` object records the PRNG algorithm and seed-derivation scheme once. The initialiser consumes only the initialisation stream; the canonical serial kernel consumes only the dynamics stream. Replicate derivation does not depend on execution order. The same base seed and replicate index therefore preserve the same initial tissue between matched E02 regimes even when their contact energies differ.

## Measurements

The primary metric is heterotypic interface fraction. It counts each unordered Moore-neighbour pair of lattice sites once, excludes pairs within one biological cell, and excludes every pair containing medium. Its denominator is the remaining interface count between distinct biological cells. A zero denominator maps to fraction 0. Supporting measurements include heterotypic and total homotypic pair counts, homotypic contacts by type, total cell-cell contact count, A/B cell counts, area and configured perimeter summaries, and connected type domains. A type domain is a connected component in the graph whose vertices are biological cells and whose edges are same-type cell interfaces in the configured measurement neighbourhood.

The E02 scientific regression gate keeps heterotypic-interface fraction primary. It also requires higher total homotypic contact under sorting than in the matched initial tissue and control, and requires each sorted replicate to retain more than one quarter of its initial cell-cell interfaces. The latter is a loose guard against catastrophic dispersal.

Cell perimeter uses the configured perimeter stencil and is a lattice-model quantity. One MCS is an update unit, not physical time. Fluctuation amplitude is not physical temperature. Contact energies are model parameters, not measured molecular adhesion forces.

## Running

From the repository root:

```sh
dotnet run --project tools/Rowles.Morphogenesis.Experiments -- run experiments/canonical/E00-single-cell-relaxation.json
dotnet run --project tools/Rowles.Morphogenesis.Experiments -- run experiments/canonical/E01-fluctuation-middle.json
dotnet run --project tools/Rowles.Morphogenesis.Experiments -- run experiments/canonical/E02-control.json
dotnet run --project tools/Rowles.Morphogenesis.Experiments -- run experiments/canonical/E02-sorting.json
```

Use `--output <path>` to choose a result file. `--replicates <count>` is a development override; the emitted result contains the resolved override. The tool does not replace seeds implicitly. Its default result path is `experiments/results/<experiment-id>.json`.

The E02 population-perturbation manifests are run in the same way. The canonical result directory holds compact outputs used to document the current qualification; larger campaigns should be written elsewhere.
