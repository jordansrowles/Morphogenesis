# Canonical experiment run record

The eight adjacent result files contain the resolved manifests, deterministic seed provenance, raw replicate measurements, enabled snapshots and summaries recomputed from the raw data. The archives were generated sequentially from the clean source revision below and contain 89 successful replicates in total: one for E00, 24 for E01 and 64 for E02.

## Source and run environment

- Source commit: `4a9c2c6befd7e9510a6c679103614007ccc1b78d`; source tree state: `clean`.
- .NET SDK: `10.0.103`; .NET runtime: `10.0.3`; production assembly: `1.0.0.0`.
- Host: Debian GNU/Linux 13 (trixie), Linux X64.
- Execution: `canonical-serial-v1`; replicate execution is sequential.
- PRNG: `xoshiro256** with SplitMix64`. SplitMix64 expands each seed into the four xoshiro state words.
- Experiment seed policy: `splitmix64-domain-v1`.

The seed policy derives replicate seed `r` as `Mix(baseSeed + 0x9E3779B97F4A7C15 × (replicateIndex + 1))`, with unchecked 64-bit arithmetic and the SplitMix64 finaliser. It derives separate initialisation and dynamics seeds as `Mix(r XOR domainTag)`, using the fixed `INITIAL1` and `DYNAMIC1` tags. If those two derived values collide, the low bit of the dynamics seed is toggled. Each result records the base seed, replicate seeds, both derived seeds and the exact random algorithm.

E00 uses base seed `1000`. Every E01 and E02 manifest uses base seed `20260929`. The result reader recomputes replicate seeds, derived seeds and every aggregate statistic from raw replicate data. The archive tests also require clean source provenance, a common source commit, successful replicates, correct measurement and snapshot schedules, valid snapshots, and identical initial tissues for matched E02 conditions.

## Canonical cases and parameters

All experiments use periodic boundaries, von Neumann copy proposals, Moore contact and perimeter stencils, von Neumann connectivity checks, and Moore interface measurements. E00 uses area target 16 with stiffness 1.25. E01 and E02 use area target 16 with stiffness 1. The E01 and E02 cell perimeter target is 44 with stiffness 0.1. E00 measures at MCS zero, every five MCS and its final state; snapshots are taken at MCS zero, every ten MCS and the final state. E01 and E02 measure at MCS zero, their configured cadence and the final state; their snapshot cadence is disabled.

| Case | Setup | Grid | MCS | Replicates | Fluctuation | Base seed |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| E00 | One type-A cell; smoke case | 20×20 | 40 | 1 | 8 | 1000 |
| E01 low | 16 cells; fluctuation sweep | 32×32 | 40 | 8 | 2 | 20260929 |
| E01 middle | 16 cells; fluctuation sweep | 32×32 | 40 | 8 | 8 | 20260929 |
| E01 high | 16 cells; fluctuation sweep | 32×32 | 40 | 8 | 24 | 20260929 |
| E02 control | 16 cells; equal cell-cell contacts | 32×32 | 80 | 16 | 12 | 20260929 |
| E02 sorting | 16 cells; higher A-B contact | 32×32 | 80 | 16 | 12 | 20260929 |
| E02 control perturbation | 20 cells; equal cell-cell contacts | 36×36 | 80 | 16 | 12 | 20260929 |
| E02 sorting perturbation | 20 cells; higher A-B contact | 36×36 | 80 | 16 | 12 | 20260929 |

The packed initialiser places equal numbers of type-A and type-B cells in E01 and E02. All cells have target area 16. In E02 the control contact matrix, ordered medium/A/B, is `[[0,10,10],[10,2,2],[10,2,2]]`; the sorting matrix changes only the symmetric A-B and B-A terms to 20. Matched control and sorting replicates use the same initialisation seed, dynamics seed, initial cell occupancy and type assignment. Only the contact-energy matrix differs within each matched pair.

## Measurements and scientific outcomes

The primary metric is the heterotypic interface fraction. It counts every unordered Moore-neighbour pair of lattice sites once, excludes pairs within a single biological cell and pairs containing medium, then divides heterotypic cell-cell pairs by all remaining cell-cell pairs. A zero denominator yields zero. The JSON files retain the raw numerator and denominator. Supporting measurements include homotypic contacts by type, total cell-cell interfaces, accepted-attempt fraction, cell counts, area and configured perimeter summaries, and connected type domains.

Statistics use successful replicates and report count, mean, median, sample standard deviation, minimum and maximum. The full-run summaries are:

| Case | Final heterotypic fraction: mean | Median | Sample SD | Range | Accepted-attempt fraction: mean |
| --- | ---: | ---: | ---: | ---: | ---: |
| E01 low | 0.55285 | 0.53726 | 0.06689 | 0.44867–0.63774 | 0.01432 |
| E01 middle | 0.52570 | 0.53780 | 0.10120 | 0.33333–0.68182 | 0.03962 |
| E01 high | 0.57161 | 0.52290 | 0.11032 | 0.45603–0.78049 | 0.06057 |
| E02 control, 16 cells | 0.52575 | 0.49742 | 0.12025 | 0.35783–0.79286 | 0.04625 |
| E02 sorting, 16 cells | 0.16227 | 0.16751 | 0.03003 | 0.11583–0.22870 | 0.04202 |
| E02 control, 20 cells | 0.54017 | 0.55929 | 0.09867 | 0.30362–0.71618 | 0.04588 |
| E02 sorting, 20 cells | 0.16543 | 0.16723 | 0.04043 | 0.08696–0.22393 | 0.04113 |

At both populations, the sorting median is below the control median by at least 0.15: the observed gaps are `0.32992` for 16 cells and `0.39206` for 20 cells. The full-run ranges are also separated: the sorting maximum remains below the control minimum by `0.12913` and `0.07969`, respectively. The reduced scientific regression uses 50 MCS and eight replicates per condition and retains the `0.15` median-gap threshold.

The supporting anti-dispersal gate requires the sorting median total homotypic interface count to exceed both its matched initial value and the final control value. Those medians are 119 initially, 187 under sorting and 142 in the 16-cell control; for the population perturbation they are 156.5, 257.5 and 167.5. Every full-run sorted replicate retains more than one quarter of its starting cell-cell interfaces: the minimum observed retention is `71.7%` at 16 cells and `81.4%` at 20 cells. These thresholds are intentionally loose and guard against catastrophic dispersal.

The E01 accepted-attempt fraction rises with fluctuation amplitude. Its heterotypic fraction is not monotone, so these results do not support a monotone sorting claim for E01. E00 preserved its one cell through 40 MCS; final area was 9 lattice sites and configured perimeter was 32. E00 is a mechanics and runner smoke case, not a biological calibration.

## Bounded comparison with Artistoo

The comparison is to the official Artistoo cell-sorting explorable, its CPM tutorial and the published Artistoo paper. The paper records version 1.0.0 and describes a cell-sorting run with 50 cells of each type on a 200×200 grid, seeded within a circle of radius 67 and run for 2,000 MCS. The official repository package manifest reports version 1.2.0 when checked on 2026-10-01. The live explorable does not publish a pinned software revision or a complete machine-readable parameter manifest.

| Comparison item | This experiment | Artistoo source | Limit |
| --- | --- | --- | --- |
| Population and grid | 16 cells on 32×32; perturbation uses 20 on 36×36. | Published run uses 50 cells of each type on 200×200, seeded within radius 67. | Population, density and initial geometry differ. |
| Neighbourhood | Von Neumann copy proposal; Moore contact, perimeter and measurement; von Neumann connectivity. | CPM tutorial uses Moore proposal targets and contact coupling. | Proposal and connectivity rules differ. |
| Boundary | Periodic on both axes. | The manual documents periodic `torus` boundaries as the default; the explorable does not expose a pinned run configuration. | The default aligns, but the exact explorable setting is unconfirmed. |
| Energies | Control uses equal A-A, B-B and A-B contacts; sorting raises A-B from 2 to 20. Both include perimeter mechanics. | The explorable describes `J(A,A), J(B,B) < J(A,B)` and suggests background contacts of 8 with unlike-type contact raised to 12. | The direction matches; full energy settings and perimeter mechanics do not. |
| Dynamics and metric | Canonical serial kernel; 1,024 or 1,296 attempted copies per MCS; heterotypic interface fraction. | Tutorial uses one attempt per grid pixel per MCS, Moore proposals and Metropolis acceptance. The explorable reports visible same-type patches rather than this normalized interface statistic. | The result supports a qualitative direction only, not a trajectory or numeric comparison. |

Artistoo describes same-type patches when unlike-type contacts are raised. The reduced regression and both full populations show lower heterotypic interface fractions in the sorting regime, consistent with that qualitative behaviour. No direct numerical comparison is claimed because the starting tissues, Hamiltonian, proposal schedule and metric differ. A like-for-like comparison would require pinning an Artistoo revision and matching its initial states, energy terms, update schedule and measurement definition.

Sources: [Artistoo paper](https://pmc.ncbi.nlm.nih.gov/articles/PMC8143789/), [official cell-sorting explorable](https://artistoo.net/explorables/Explorable-CellSorting.html), [CPM tutorial](https://artistoo.net/explorables/Explorable-CPM.html), [simulation configuration manual](https://artistoo.net/manual/simulationConfig.html), and [official package manifest](https://raw.githubusercontent.com/ingewortel/artistoo/master/package.json).

## Performance observations

These are single sequential archive runs on the host listed above. They are observations, not benchmark claims. Rates use lattice-site copy attempts (`width × height × MCS × replicates`) divided by ensemble wall time. Timing and allocation values vary with host load and runtime state. Connectivity fallbacks are counted, but fallback time is not instrumented.

| Ensemble | Grid | Replicates × MCS | Wall time | Attempts/s | MCS/s | Measurement time | Allocated bytes | Bytes/attempt | Connectivity fallbacks |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| E02 control | 32×32 | 16 × 80 | 655.94 ms | 1,998,226 | 1,951 | 53.15 ms | 16,510,920 | 12.60 | 13,915 |
| E02 sorting | 32×32 | 16 × 80 | 659.60 ms | 1,987,136 | 1,941 | 54.83 ms | 16,689,288 | 12.73 | 13,746 |
| E02 control perturbation | 36×36 | 16 × 80 | 727.60 ms | 2,279,921 | 1,759 | 48.59 ms | 25,467,000 | 15.35 | 17,837 |
| E02 sorting perturbation | 36×36 | 16 × 80 | 508.25 ms | 3,263,884 | 2,518 | 40.67 ms | 25,702,936 | 15.49 | 17,248 |

## Validation and interpretation limits

The five-project Release build completed with zero warnings and zero errors using `dotnet build Rowles.Morphogenesis.slnx --no-restore --configuration Release --disable-build-servers -m:1 -p:MSBuildEnableWorkloadResolver=false`.

Local Linux validation passed:

- Ordinary solution tests: 173 passed (85 reference and 88 production).
- Scientific regression: 2 passed.
- Canonical archive tests: 2 passed, including validation of all eight result files.
- Complete solution suite: 175 passed, zero failed, zero skipped (85 reference and 90 production).

The hosted workflow checks the complete solution on `ubuntu-latest` and `windows-latest`, and runs the reduced scientific regression on `ubuntu-latest`. Check the archive evidence commit's GitHub Actions status for those hosted results. Each result file records the same clean source revision; adding the result files and this run record changes the repository afterward, as expected.

One MCS is an update unit, not physical time. Fluctuation amplitude is not physical temperature. Contact energies are model parameters, not measured molecular adhesion forces. The E02 conclusion is limited to these manifests, seeds, mechanics and measurement definitions. Artistoo's unpublished deployed revision, full interactive parameter set and replicate samples prevent a direct numeric comparison.
