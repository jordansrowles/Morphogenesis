using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Model;
using Rowles.Morphogenesis.Random;
using Rowles.Morphogenesis.Topology;
using ReferenceBruteForceMove = Rowles.Morphogenesis.Reference.Energy.BruteForceMoveReference;
using ReferenceCellDefinition = Rowles.Morphogenesis.Reference.Model.CellDefinition;
using ReferenceCellTypeDefinition = Rowles.Morphogenesis.Reference.Model.CellTypeDefinition;
using ReferenceContactEnergyMatrix = Rowles.Morphogenesis.Reference.Model.ContactEnergyMatrix;
using ReferenceEnergy = Rowles.Morphogenesis.Reference.Energy.ReferenceEnergy;
using ReferenceGridPoint = Rowles.Morphogenesis.Reference.Lattice.GridPoint;
using ReferenceModelConventions = Rowles.Morphogenesis.Reference.Lattice.ModelConventions;
using ReferenceState = Rowles.Morphogenesis.Reference.Model.ReferenceState;

namespace Rowles.Morphogenesis.Tests.Dynamics;

public sealed class GeneratedInvariantTests
{
    private static readonly ulong[] CaseSeeds =
    [
        0x1001UL, 0x2002UL, 0x3003UL, 0x4004UL,
        0x5005UL, 0x6006UL, 0x7007UL, 0x8008UL,
        0x9009UL, 0xA00AUL, 0xB00BUL, 0xC00CUL
    ];

    private static readonly (int X, int Y)[] CardinalOffsets =
    [
        (0, -1), (1, 0), (0, 1), (-1, 0)
    ];

    private static readonly (int X, int Y)[] MooreOffsets =
    [
        (-1, -1), (0, -1), (1, -1), (-1, 0),
        (1, 0), (-1, 1), (0, 1), (1, 1)
    ];

    [Fact]
    public void P00_generated_periodic_local_deltas_match_reference_and_exact_perimeter_changes()
    {
        foreach (ulong seed in CaseSeeds)
        {
            GeneratedCase generated = GenerateCase(seed, BoundaryMode.Periodic);
            MorphogenesisState production = CreateProduction(generated);
            ReferenceState reference = CreateReference(generated);

            for (int target = 0; target < production.SiteCount; target++)
            {
                int targetX = target % production.Width;
                int targetY = target / production.Width;
                foreach ((int dx, int dy) in CardinalOffsets)
                {
                    int sourceX = Wrap(targetX + dx, production.Width);
                    int sourceY = Wrap(targetY + dy, production.Height);
                    int source = sourceY * production.Width + sourceX;
                    int oldId = production.CellIdAt(target);
                    int newId = production.CellIdAt(source);
                    if (oldId == newId)
                    {
                        continue;
                    }

                    ReferenceGridPoint targetPoint = new(targetX, targetY);
                    ReferenceGridPoint sourcePoint = new(sourceX, sourceY);
                    Rowles.Morphogenesis.Reference.Energy.BruteForceMove expected =
                        ReferenceBruteForceMove.Evaluate(reference, sourcePoint, targetPoint);
                    MoveEvaluation actual = EnergyDeltaCalculator.Evaluate(production, target, newId);

                    AssertTermsEqual(expected.Terms, actual.Terms, generated, target, source, "P00 terms");

                    ReferenceState changed = reference.Clone();
                    int expectedOldPerimeterDelta = 0;
                    int expectedNewPerimeterDelta = 0;
                    if (oldId > 0)
                    {
                        int before = ReferenceEnergy.ComputePerimeter(reference, oldId);
                        changed.SetCellId(targetPoint, newId);
                        int after = ReferenceEnergy.ComputePerimeter(changed, oldId);
                        expectedOldPerimeterDelta = after - before;
                        changed.SetCellId(targetPoint, oldId);
                    }

                    if (newId > 0)
                    {
                        int before = ReferenceEnergy.ComputePerimeter(reference, newId);
                        changed.SetCellId(targetPoint, newId);
                        int after = ReferenceEnergy.ComputePerimeter(changed, newId);
                        expectedNewPerimeterDelta = after - before;
                    }

                    Assert.True(
                        actual.OldCellPerimeterDelta == expectedOldPerimeterDelta &&
                        actual.NewCellPerimeterDelta == expectedNewPerimeterDelta,
                        FailureContext(
                            generated,
                            -1,
                            target,
                            source,
                            oldId,
                            newId,
                            $"P00 perimeter deltas expected old/new {expectedOldPerimeterDelta}/{expectedNewPerimeterDelta}, actual {actual.OldCellPerimeterDelta}/{actual.NewCellPerimeterDelta}"));
                }
            }
        }
    }

    [Fact]
    public void P01_generated_transition_sequences_preserve_exact_state_and_exercise_expected_paths()
    {
        Coverage coverage = new();
        int caseIndex = 0;
        foreach (ulong seed in CaseSeeds)
        {
            BoundaryMode boundary = caseIndex++ % 2 == 0 ? BoundaryMode.Periodic : BoundaryMode.Wall;
            GeneratedCase generated = GenerateCase(seed, boundary);
            MorphogenesisState state = CreateProduction(generated);
            SerialSimulation simulation = new(state, seed ^ 0xD1CEB00CUL, 5);

            int attempts = state.SiteCount * 8;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                int[] beforeLattice = state.GetCellIdsCopy();
                CellState[] beforeCells = SnapshotCells(state, generated.Cells);
                AttemptResult result = simulation.Attempt();
                coverage.Observe(result, generated.Cells);

                if (result.Status == AttemptStatus.Accepted)
                {
                    AssertExactState(state, generated, attempt, result);
                }
                else
                {
                    Assert.True(
                        beforeLattice.SequenceEqual(state.GetCellIdsCopy()),
                        FailureContext(generated, attempt, result.TargetIndex, result.SourceIndex, result.OldCellId, result.NewCellId, "Rejected/no-op lattice mutation"));
                    CellState[] afterCells = SnapshotCells(state, generated.Cells);
                    Assert.True(
                        beforeCells.SequenceEqual(afterCells),
                        FailureContext(generated, attempt, result.TargetIndex, result.SourceIndex, result.OldCellId, result.NewCellId, "Rejected/no-op cell-runtime mutation"));
                    AssertExactState(state, generated, attempt, result);
                }
            }
        }

        Assert.True(coverage.Accepted, "Generated corpus did not observe an accepted move.");
        Assert.True(coverage.MetropolisRejected, "Generated corpus did not observe a Metropolis rejection.");
        Assert.True(coverage.NoOp, "Generated corpus did not observe a no-op.");
        Assert.True(coverage.CellToMedium, "Generated corpus did not observe a cell-to-medium proposal.");
        Assert.True(coverage.MediumToCell, "Generated corpus did not observe a medium-to-cell proposal.");
        Assert.True(coverage.CellToCell, "Generated corpus did not observe a cell-to-cell proposal.");
        Assert.True(coverage.SameTypeDifferentId, "Generated corpus did not observe same-type/different-ID interaction.");
        Assert.True(coverage.DifferentType, "Generated corpus did not observe different-type interaction.");
        Assert.True(
            coverage.ConnectivityRejected || coverage.ConnectivityFallback,
            "Generated corpus did not exercise connectivity rejection or global fallback.");
    }

    [Fact]
    public void P02_periodic_generated_runs_match_reference_for_every_energy_evaluated_proposal()
    {
        foreach (ulong seed in CaseSeeds.Take(8))
        {
            GeneratedCase generated = GenerateCase(seed, BoundaryMode.Periodic);
            MorphogenesisState state = CreateProduction(generated);
            SerialSimulation simulation = new(state, seed ^ 0xC0DEC0DEUL, 5);

            int attempts = state.SiteCount * 6;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                int[] before = state.GetCellIdsCopy();
                AttemptResult result = simulation.Attempt();
                if (result.Status == AttemptStatus.NoOp ||
                    result.RejectionReason is RejectionReason.FinalSite or RejectionReason.Disconnected or RejectionReason.FixedWall)
                {
                    continue;
                }

                GeneratedCase beforeCase = generated with { Ids = before };
                ReferenceState reference = CreateReference(beforeCase);
                ReferenceGridPoint target = new(result.TargetIndex % state.Width, result.TargetIndex / state.Width);
                ReferenceGridPoint source = new(result.SourceIndex % state.Width, result.SourceIndex / state.Width);
                Rowles.Morphogenesis.Reference.Energy.BruteForceMove expected =
                    ReferenceBruteForceMove.Evaluate(reference, source, target);

                AssertTermsEqual(expected.Terms, result.DeltaH, generated, result.TargetIndex, result.SourceIndex, "P02 terms");

                if (result.Status == AttemptStatus.Accepted)
                {
                    int[] expectedIds = (int[])before.Clone();
                    expectedIds[result.TargetIndex] = result.NewCellId;
                    Assert.True(
                        expectedIds.SequenceEqual(state.GetCellIdsCopy()),
                        FailureContext(generated, attempt, result.TargetIndex, result.SourceIndex, result.OldCellId, result.NewCellId, "P02 accepted transition mismatch"));
                }
            }
        }
    }

    [Fact]
    public void P03_long_seeded_generated_runs_preserve_invariants()
    {
        for (int index = 0; index < 8; index++)
        {
            ulong seed = CaseSeeds[index];
            BoundaryMode boundary = index % 2 == 0 ? BoundaryMode.Periodic : BoundaryMode.Wall;
            GeneratedCase generated = GenerateCase(seed, boundary);
            MorphogenesisState state = CreateProduction(generated);
            SerialSimulation simulation = new(state, seed ^ 0x5EED5EEDUL, 7);

            int acceptedSeen = 0;
            for (int mcs = 0; mcs < 10; mcs++)
            {
                for (int attempt = 0; attempt < state.SiteCount; attempt++)
                {
                    AttemptResult result = simulation.Attempt();
                    if (result.Status == AttemptStatus.Accepted && (acceptedSeen++ % 7 == 0))
                    {
                        AssertExactState(state, generated, mcs * state.SiteCount + attempt, result);
                    }
                }

                state.ValidateInvariants();
                AssertExactGeometry(state, generated, mcs, "P03 MCS");
            }
        }
    }

    private static GeneratedCase GenerateCase(ulong seed, BoundaryMode boundary)
    {
        CaseRng rng = new(seed);
        int width = 7 + rng.NextInt(3);
        int height = 7 + rng.NextInt(3);
        int[] ids = new int[width * height];
        List<int>[] sites = [[], [], [], []];

        (int X, int Y)[] anchors =
        [
            default,
            boundary == BoundaryMode.Wall ? (0, 1) : (1, 1),
            (width - 2, 1),
            (width / 2, height - 2)
        ];

        for (int cellId = 1; cellId <= 3; cellId++)
        {
            int index = anchors[cellId].Y * width + anchors[cellId].X;
            ids[index] = cellId;
            sites[cellId].Add(index);
        }

        for (int cellId = 1; cellId <= 3; cellId++)
        {
            int targetSize = 4 + rng.NextInt(4);
            int guard = 0;
            while (sites[cellId].Count < targetSize && guard++ < 256)
            {
                int from = sites[cellId][rng.NextInt(sites[cellId].Count)];
                int x = from % width;
                int y = from / width;
                (int dx, int dy) = CardinalOffsets[rng.NextInt(CardinalOffsets.Length)];
                int nx = x + dx;
                int ny = y + dy;
                if (boundary == BoundaryMode.Periodic)
                {
                    nx = Wrap(nx, width);
                    ny = Wrap(ny, height);
                }
                else if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                {
                    continue;
                }

                int next = ny * width + nx;
                if (ids[next] != 0)
                {
                    continue;
                }

                ids[next] = cellId;
                sites[cellId].Add(next);
            }

            if (sites[cellId].Count < 4)
            {
                throw new InvalidOperationException($"Generator failed for seed 0x{seed:X16}, cell {cellId}.");
            }
        }

        CellDefinition[] cells =
        [
            new(1, 1, sites[1].Count + rng.NextInt(3) - 1, 0.65 + 0.05 * rng.NextInt(4), 18 + rng.NextInt(7), 0.05 + 0.01 * rng.NextInt(4)),
            new(2, 1, sites[2].Count + rng.NextInt(3) - 1, 0.70 + 0.05 * rng.NextInt(4), 18 + rng.NextInt(7), 0.05 + 0.01 * rng.NextInt(4)),
            new(3, 2, sites[3].Count + rng.NextInt(3) - 1, 0.75 + 0.05 * rng.NextInt(4), 18 + rng.NextInt(7), 0.05 + 0.01 * rng.NextInt(4))
        ];

        double[,] contacts =
        {
            { 0, 2 + 0.1 * rng.NextInt(4), 3 + 0.1 * rng.NextInt(4) },
            { 0, 1 + 0.1 * rng.NextInt(4), 2.2 + 0.1 * rng.NextInt(4) },
            { 0, 0, 1.2 + 0.1 * rng.NextInt(4) }
        };
        contacts[1, 0] = contacts[0, 1];
        contacts[2, 0] = contacts[0, 2];
        contacts[2, 1] = contacts[1, 2];

        return new GeneratedCase(seed, width, height, ids, cells, contacts, boundary);
    }

    private static MorphogenesisState CreateProduction(GeneratedCase generated) => new(
        generated.Width,
        generated.Height,
        generated.Ids,
        generated.Cells,
        new ContactEnergyMatrix(generated.Contacts),
        new SimulationConfiguration(generated.Boundary, LatticeConventions.Canonical));

    private static ReferenceState CreateReference(GeneratedCase generated)
    {
        int[,] lattice = new int[generated.Width, generated.Height];
        for (int y = 0; y < generated.Height; y++)
        {
            for (int x = 0; x < generated.Width; x++)
            {
                lattice[x, y] = generated.Ids[y * generated.Width + x];
            }
        }

        ReferenceCellDefinition[] cells = generated.Cells
            .Select(cell => new ReferenceCellDefinition(
                cell.CellId,
                cell.CellTypeId,
                cell.TargetArea,
                cell.AreaStiffness,
                cell.TargetPerimeter,
                cell.PerimeterStiffness))
            .ToArray();

        ReferenceCellTypeDefinition[] types =
        [
            new(0, "Medium"),
            new(1, "Type 1"),
            new(2, "Type 2")
        ];

        return new ReferenceState(
            lattice,
            cells,
            types,
            new ReferenceContactEnergyMatrix(generated.Contacts),
            ReferenceModelConventions.Canonical);
    }

    private static void AssertExactState(
        MorphogenesisState state,
        GeneratedCase generated,
        int attempt,
        AttemptResult result)
    {
        state.ValidateInvariants();
        AssertExactGeometry(state, generated, attempt, "transition");

        foreach (CellDefinition definition in generated.Cells)
        {
            Assert.True(
                CellConnectivity.CountComponents(state, definition.CellId) == 1,
                FailureContext(generated, attempt, result.TargetIndex, result.SourceIndex, result.OldCellId, result.NewCellId, $"Cell {definition.CellId} disconnected"));
        }
    }

    private static void AssertExactGeometry(MorphogenesisState state, GeneratedCase generated, int step, string category)
    {
        foreach (CellDefinition definition in generated.Cells)
        {
            CellState cell = state.GetCellState(definition.CellId);
            int exactArea = 0;
            int exactPerimeter = 0;
            for (int index = 0; index < state.SiteCount; index++)
            {
                if (state.CellIdAt(index) != definition.CellId)
                {
                    continue;
                }

                exactArea++;
                int x = index % state.Width;
                int y = index / state.Width;
                foreach ((int dx, int dy) in MooreOffsets)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if (generated.Boundary == BoundaryMode.Wall &&
                        ((uint)nx >= (uint)state.Width || (uint)ny >= (uint)state.Height))
                    {
                        exactPerimeter++;
                        continue;
                    }

                    nx = Wrap(nx, state.Width);
                    ny = Wrap(ny, state.Height);
                    if (state.CellIdAt(ny * state.Width + nx) != definition.CellId)
                    {
                        exactPerimeter++;
                    }
                }
            }

            Assert.True(
                cell.Area == exactArea && cell.Perimeter == exactPerimeter,
                FailureContext(generated, step, -1, -1, definition.CellId, definition.CellId,
                    $"{category}: cell {definition.CellId} expected area/perimeter {exactArea}/{exactPerimeter}, tracked {cell.Area}/{cell.Perimeter}"));
        }
    }

    private static CellState[] SnapshotCells(MorphogenesisState state, CellDefinition[] cells) =>
        cells.Select(cell => state.GetCellState(cell.CellId)).ToArray();

    private static void AssertTermsEqual(
        Rowles.Morphogenesis.Reference.Energy.HamiltonianBreakdown expected,
        HamiltonianBreakdown actual,
        GeneratedCase generated,
        int target,
        int source,
        string category)
    {
        AssertClose(expected.Contact, actual.Contact, generated, target, source, category + " contact");
        AssertClose(expected.Area, actual.Area, generated, target, source, category + " area");
        AssertClose(expected.Perimeter, actual.Perimeter, generated, target, source, category + " perimeter");
        AssertClose(expected.Total, actual.Total, generated, target, source, category + " total");
    }

    private static void AssertClose(
        double expected,
        double actual,
        GeneratedCase generated,
        int target,
        int source,
        string category)
    {
        bool close = Math.Abs(expected - actual) <= 1e-10 + 1e-12 * Math.Max(Math.Abs(expected), Math.Abs(actual));
        Assert.True(
            close,
            FailureContext(
                generated,
                -1,
                target,
                source,
                target >= 0 ? generated.Ids[target] : -1,
                source >= 0 ? generated.Ids[source] : -1,
                $"{category}: expected {expected:R}, actual {actual:R}"));
    }

    private static string FailureContext(
        GeneratedCase generated,
        int step,
        int target,
        int source,
        int oldId,
        int newId,
        string category) =>
        $"{category}; seed=0x{generated.Seed:X16}; size={generated.Width}x{generated.Height}; " +
        $"boundary={generated.Boundary}; step={step}; target={target}; source={source}; oldId={oldId}; newId={newId}; " +
        $"lattice={string.Join(',', generated.Ids)}; cells={string.Join('|', generated.Cells.Select(c => c.ToString()))}; " +
        $"contacts={MatrixString(generated.Contacts)}";

    private static string MatrixString(double[,] matrix)
    {
        List<string> rows = [];
        for (int row = 0; row < matrix.GetLength(0); row++)
        {
            List<string> values = [];
            for (int column = 0; column < matrix.GetLength(1); column++)
            {
                values.Add(matrix[row, column].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }

            rows.Add(string.Join(",", values));
        }

        return string.Join(";", rows);
    }

    private static int Wrap(int value, int size)
    {
        int wrapped = value % size;
        return wrapped < 0 ? wrapped + size : wrapped;
    }

    private sealed record GeneratedCase(
        ulong Seed,
        int Width,
        int Height,
        int[] Ids,
        CellDefinition[] Cells,
        double[,] Contacts,
        BoundaryMode Boundary);

    private sealed class Coverage
    {
        internal bool Accepted { get; private set; }
        internal bool MetropolisRejected { get; private set; }
        internal bool ConnectivityRejected { get; private set; }
        internal bool ConnectivityFallback { get; private set; }
        internal bool NoOp { get; private set; }
        internal bool CellToMedium { get; private set; }
        internal bool MediumToCell { get; private set; }
        internal bool CellToCell { get; private set; }
        internal bool SameTypeDifferentId { get; private set; }
        internal bool DifferentType { get; private set; }

        internal void Observe(AttemptResult result, CellDefinition[] cells)
        {
            Accepted |= result.Status == AttemptStatus.Accepted;
            MetropolisRejected |= result.RejectionReason == RejectionReason.Metropolis;
            ConnectivityRejected |= result.RejectionReason == RejectionReason.Disconnected;
            ConnectivityFallback |= result.UsedGlobalConnectivityFallback;
            NoOp |= result.Status == AttemptStatus.NoOp;
            CellToMedium |= result.OldCellId > 0 && result.NewCellId == 0;
            MediumToCell |= result.OldCellId == 0 && result.NewCellId > 0;
            CellToCell |= result.OldCellId > 0 && result.NewCellId > 0 && result.OldCellId != result.NewCellId;

            if (result.OldCellId > 0 && result.NewCellId > 0 && result.OldCellId != result.NewCellId)
            {
                int oldType = cells.Single(cell => cell.CellId == result.OldCellId).CellTypeId;
                int newType = cells.Single(cell => cell.CellId == result.NewCellId).CellTypeId;
                SameTypeDifferentId |= oldType == newType;
                DifferentType |= oldType != newType;
            }
        }
    }

    private struct CaseRng(ulong seed)
    {
        private ulong _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

        internal int NextInt(int exclusiveUpperBound)
        {
            if (exclusiveUpperBound <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
            }

            ulong value = NextUInt64();
            return (int)(value % (ulong)exclusiveUpperBound);
        }

        private ulong NextUInt64()
        {
            ulong x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return x * 0x2545F4914F6CDD1DUL;
        }
    }
}
