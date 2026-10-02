"""Frozen-band, paired-seed analysis for the M3 event-clock experiments."""
import argparse
import csv
import hashlib
import json
import math
import random
import statistics
from pathlib import Path

METRICS = ['mixing', 'heterotypic', 'total_interface', 'homotypic_a', 'homotypic_b',
           'domains_a', 'domains_b', 'largest_domain_a', 'largest_domain_b',
           'area_mean', 'area_sd', 'area_q10', 'area_q50', 'area_q90',
           'perimeter_mean', 'perimeter_sd', 'perimeter_q10', 'perimeter_q50',
           'perimeter_q90', 'shape_mean', 'occupied_fraction', 'acceptance_fraction']
CONDITIONS = ['sorting-32-t6', 'sorting-32-t12', 'sorting-32-t24', 'sorting-64-t12',
              'control-32-t12', 'control-64-t12', 'sorting-wall-32-t12', 'high-32-t24']


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read_capture(root, kernel, seeds):
    path = root / f'{kernel}-samples-{seeds}.csv'
    groups = {}
    with path.open() as source:
        for row in csv.DictReader(source):
            assert row['kernel'] == kernel
            condition, mcs, replicate = row['condition'], int(row['mcs']), int(row['replicate'])
            assert condition in CONDITIONS and 0 <= replicate < seeds
            key = (condition, mcs)
            assert replicate not in groups.setdefault(key, {})
            assert all(math.isfinite(float(row[metric])) for metric in METRICS)
            groups[key][replicate] = row
    assert len(groups) == 39
    for condition in CONDITIONS:
        checkpoints = [0, 10, 20, 40] if condition == 'high-32-t24' else [0, 10, 20, 40, 80]
        for mcs in checkpoints:
            assert set(groups[condition, mcs]) == set(range(seeds))
    return groups


def quantile(values, fraction):
    ordered = sorted(values)
    index = (len(ordered) - 1) * fraction
    lower = int(index)
    upper = min(lower + 1, len(ordered) - 1)
    if ordered[lower] == ordered[upper]:
        return ordered[lower]
    if math.isinf(ordered[upper]):
        return math.inf
    return ordered[lower] + (ordered[upper] - ordered[lower]) * (index - lower)


def variance_interval(x, y, key):
    vx, vy = statistics.variance(x), statistics.variance(y)
    if vx == 0:
        return (1, 1, 1) if vy == 0 else (math.inf, math.inf, math.inf)
    ratio = vy / vx
    rng = random.Random(int(hashlib.sha256(key.encode()).hexdigest()[:16], 16))
    ratios = []
    n = len(x)
    for _ in range(512):
        indexes = [rng.randrange(n) for _ in range(n)]
        bx, by = [x[i] for i in indexes], [y[i] for i in indexes]
        sx, sy = statistics.variance(bx), statistics.variance(by)
        ratios.append(sy / sx if sx > 0 else (1 if sy == 0 else math.inf))
    return ratio, quantile(ratios, 0.05), quantile(ratios, 0.95)


def freeze(root, protocol):
    output = root / 'canonical-bands-64.csv'
    assert not output.exists(), 'Existing bands must remain frozen'
    assert not list(root.glob('border-samples-*.csv')) and not list(root.glob('directed-samples-*.csv'))
    groups = read_capture(root, 'canonical', 64)
    with output.open('w') as target:
        writer = csv.writer(target)
        writer.writerow(['condition', 'mcs', 'metric', 'seeds', 'canonical_mean', 'canonical_sd', 'half_width'])
        for (condition, mcs), rows in sorted(groups.items()):
            for metric in METRICS:
                values = [float(rows[i][metric]) for i in range(64)]
                sd = statistics.stdev(values)
                writer.writerow([condition, mcs, metric, 64, statistics.mean(values), sd, 0.5 * sd])
    metadata = {'canonical_sha256': digest(root / 'canonical-samples-64.csv'),
                'bands_sha256': digest(output), 'protocol_sha256': digest(protocol),
                'analysis_sha256': digest(__file__), 'mean_band_sd_multiplier': 0.5,
                'mean_interval': '90 percent paired Student t',
                'variance_band': [0.5, 2], 'variance_bootstrap_samples': 512,
                'variance_interval': '90 percent percentile bootstrap, paired seed clusters'}
    (root / 'frozen-band-provenance.json').write_text(json.dumps(metadata, indent=2) + '\n')
    print('Frozen 858 canonical endpoint bands from eight conditions and 64 seeds before accelerated observations.')


def compare(root, kernel, seeds):
    metadata = json.loads((root / 'frozen-band-provenance.json').read_text())
    assert metadata['bands_sha256'] == digest(root / 'canonical-bands-64.csv')
    assert metadata['canonical_sha256'] == digest(root / 'canonical-samples-64.csv')
    canonical = read_capture(root, 'canonical', seeds)
    candidate = read_capture(root, kernel, seeds)
    original = read_capture(root, 'canonical', 64)
    for key in original:
        for replicate in range(64):
            assert canonical[key][replicate] == original[key][replicate], 'Canonical seed prefixes changed'
    with (root / 'canonical-bands-64.csv').open() as source:
        bands = {(r['condition'], int(r['mcs']), r['metric']): r for r in csv.DictReader(source)}
    assert len(bands) == 858
    t_critical = {64: 1.669402221706, 128: 1.656940719756, 256: 1.650850024}
    failures = []
    variance_failures = []
    with (root / f'{kernel}-comparison-{seeds}.csv').open('w') as target:
        writer = csv.writer(target)
        writer.writerow(['condition', 'mcs', 'metric', 'seeds', 'canonical_mean', 'candidate_mean',
                         'difference', 'difference_ci_low', 'difference_ci_high', 'frozen_half_width',
                         'effect_in_canonical_sd', 'mean_equivalent', 'variance_ratio',
                         'variance_ci_low', 'variance_ci_high', 'variance_equivalent'])
        for (condition, mcs), rows in sorted(canonical.items()):
            other = candidate[condition, mcs]
            for replicate in range(seeds):
                for field in ['initialisation_seed', 'dynamics_seed', 'initial_hash']:
                    assert rows[replicate][field] == other[replicate][field], 'Tissues or seed schedules differ'
            for metric in METRICS:
                x = [float(rows[i][metric]) for i in range(seeds)]
                y = [float(other[i][metric]) for i in range(seeds)]
                if mcs == 0:
                    assert x == y, 'Checkpoint zero differs'
                differences = [b - a for a, b in zip(x, y)]
                mean = statistics.mean(differences)
                error = t_critical[seeds] * statistics.stdev(differences) / math.sqrt(seeds)
                low, high = mean - error, mean + error
                band = bands[condition, mcs, metric]
                width, sd = float(band['half_width']), float(band['canonical_sd'])
                passed = low >= -width and high <= width
                effect = mean / sd if sd > 0 else (0 if mean == 0 else math.copysign(math.inf, mean))
                ratio = vlo = vhi = ''
                variance_passed = ''
                final_mcs = 40 if condition == 'high-32-t24' else 80
                if mcs == final_mcs:
                    ratio, vlo, vhi = variance_interval(x, y, f'{condition}:{metric}:{kernel}:{seeds}')
                    variance_passed = vlo >= 0.5 and vhi <= 2
                    if not variance_passed:
                        variance_failures.append([condition, mcs, metric, ratio, vlo, vhi])
                if mcs > 0 and not passed:
                    failures.append([condition, mcs, metric, low, high, width])
                writer.writerow([condition, mcs, metric, seeds, statistics.mean(x), statistics.mean(y),
                                 mean, low, high, width, effect, passed, ratio, vlo, vhi, variance_passed])
    summary = {'kernel': kernel, 'seeds_per_condition': seeds, 'paired_initialisation_exact': True,
               'dynamic_endpoints': 682, 'mean_failures': failures, 'variance_failures': variance_failures,
               'mean_equivalence_passed': not failures, 'distribution_diagnostics_passed': not variance_failures,
               'all_declared_checks_passed': not failures and not variance_failures,
               'candidate_sha256': digest(root / f'{kernel}-samples-{seeds}.csv'),
               'bands_sha256': metadata['bands_sha256']}
    (root / f'{kernel}-summary-{seeds}.json').write_text(json.dumps(summary, indent=2) + '\n')
    print(f'{kernel}, {seeds} seeds/condition: {len(failures)} mean endpoints and {len(variance_failures)} final variance intervals outside frozen bounds.')
    print('All declared checks passed:', summary['all_declared_checks_passed'])


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=['freeze', 'compare'])
    parser.add_argument('directory', type=Path)
    parser.add_argument('--protocol', type=Path)
    parser.add_argument('--kernel', choices=['border', 'directed'])
    parser.add_argument('--seeds', type=int, choices=[64, 128, 256], default=64)
    args = parser.parse_args()
    if args.mode == 'freeze':
        assert args.protocol
        freeze(args.directory, args.protocol)
    else:
        assert args.kernel
        compare(args.directory, args.kernel, args.seeds)
