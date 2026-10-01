using Rowles.Morphogenesis.Diagnostics;
using Rowles.Morphogenesis.Energy;
using Rowles.Morphogenesis.Lattice;
using Rowles.Morphogenesis.Topology;

namespace Rowles.Morphogenesis.Model;

public readonly record struct CellDefinition(
    int CellId,
    int CellTypeId,
    double TargetArea,
    double AreaStiffness,
    double TargetPerimeter,
    double PerimeterStiffness);

public readonly record struct CellState(
    int CellId,
    int CellTypeId,
    bool IsAlive,
    int Area,
    int Perimeter,
    double TargetArea,
    double AreaStiffness,
    double TargetPerimeter,
    double PerimeterStiffness);

internal struct CellRuntime
{
    internal int CellTypeId;
    internal bool IsAlive;
    internal int Area;
    internal int Perimeter;
    internal double TargetArea;
    internal double AreaStiffness;
    internal double TargetPerimeter;
    internal double PerimeterStiffness;
}

public sealed class MorphogenesisState
{
    private readonly int[] _lattice;
    internal readonly CellRuntime[] Cells;

    public MorphogenesisState(
        int width,
        int height,
        int[] cellIds,
        IReadOnlyList<CellDefinition> cells,
        ContactEnergyMatrix contactEnergies,
        SimulationConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(cellIds);
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(contactEnergies);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Lattice dimensions must be positive.");
        }

        Width = width;
        Height = height;
        SiteCount = checked(width * height);
        if (cellIds.Length != SiteCount)
        {
            throw new ArgumentException("The flat lattice length must equal width × height.", nameof(cellIds));
        }

        Configuration = configuration ?? SimulationConfiguration.PeriodicCanonical;
        ContactEnergies = contactEnergies;
        ValidateConfiguration();
        ValidatePeriodicStencils();

        int largestId = 0;
        for (int index = 0; index < cells.Count; index++)
        {
            ValidateDefinition(cells[index], contactEnergies.TypeCount);
            largestId = Math.Max(largestId, cells[index].CellId);
        }

        _lattice = (int[])cellIds.Clone();
        Cells = new CellRuntime[checked(largestId + 1)];
        for (int index = 0; index < cells.Count; index++)
        {
            CellDefinition definition = cells[index];
            if (Cells[definition.CellId].IsAlive)
            {
                throw new ArgumentException($"Cell ID {definition.CellId} has duplicate metadata.", nameof(cells));
            }

            Cells[definition.CellId] = new CellRuntime
            {
                CellTypeId = definition.CellTypeId,
                IsAlive = true,
                TargetArea = definition.TargetArea,
                AreaStiffness = definition.AreaStiffness,
                TargetPerimeter = definition.TargetPerimeter,
                PerimeterStiffness = definition.PerimeterStiffness
            };
        }

        for (int index = 0; index < _lattice.Length; index++)
        {
            int cellId = _lattice[index];
            if (cellId < 0 || cellId >= Cells.Length || (cellId > 0 && !Cells[cellId].IsAlive))
            {
                throw new ArgumentException($"Lattice site {index} refers to unregistered cell ID {cellId}.", nameof(cellIds));
            }

            if (cellId > 0)
            {
                Cells[cellId].Area++;
            }
        }

        for (int cellId = 1; cellId < Cells.Length; cellId++)
        {
            if (Cells[cellId].IsAlive && Cells[cellId].Area == 0)
            {
                throw new ArgumentException($"Live cell ID {cellId} must occupy at least one lattice site.", nameof(cells));
            }
        }

        for (int index = 0; index < _lattice.Length; index++)
        {
            int cellId = _lattice[index];
            if (cellId == 0)
            {
                continue;
            }

            int perimeter = 0;
            for (int offsetIndex = 0; offsetIndex < StencilGeometry.Count(Configuration.Conventions.PerimeterNeighbourhood); offsetIndex++)
            {
                StencilGeometry.PerimeterOffset(Configuration.Conventions.PerimeterNeighbourhood, offsetIndex, out int dx, out int dy);
                int neighbour = Resolve(index, dx, dy);
                if (neighbour < 0 || _lattice[neighbour] != cellId)
                {
                    perimeter++;
                }
            }

            Cells[cellId].Perimeter += perimeter;
        }

        for (int cellId = 1; cellId < Cells.Length; cellId++)
        {
            if (Cells[cellId].IsAlive && CellConnectivity.CountComponents(this, cellId) != 1)
            {
                throw new ArgumentException($"Initial cell ID {cellId} must be connected under the configured adjacency.", nameof(cellIds));
            }
        }
    }

    public int Width { get; }

    public int Height { get; }

    public int SiteCount { get; }

    public SimulationConfiguration Configuration { get; }

    public ContactEnergyMatrix ContactEnergies { get; }

    public int CellIdAt(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "The requested site is outside the lattice.");
        }

        return _lattice[y * Width + x];
    }

    public int CellIdAt(int linearIndex)
    {
        if ((uint)linearIndex >= (uint)SiteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(linearIndex));
        }

        return _lattice[linearIndex];
    }

    public CellState GetCellState(int cellId)
    {
        if (cellId <= 0 || cellId >= Cells.Length || !Cells[cellId].IsAlive)
        {
            throw new ArgumentOutOfRangeException(nameof(cellId), "The ID does not identify a live biological cell.");
        }

        CellRuntime cell = Cells[cellId];
        return new CellState(
            cellId,
            cell.CellTypeId,
            cell.IsAlive,
            cell.Area,
            cell.Perimeter,
            cell.TargetArea,
            cell.AreaStiffness,
            cell.TargetPerimeter,
            cell.PerimeterStiffness);
    }

    public int[] GetCellIdsCopy() => (int[])_lattice.Clone();

    public HamiltonianBreakdown RecomputeHamiltonian() => InvariantValidator.RecomputeHamiltonian(this);

    public void ValidateInvariants() => InvariantValidator.Validate(this);

    internal int CellTypeId(int cellId) => cellId == 0 ? 0 : Cells[cellId].CellTypeId;

    internal int Resolve(int linearIndex, int dx, int dy)
    {
        int x = linearIndex % Width + dx;
        int y = linearIndex / Width + dy;
        if ((uint)x < (uint)Width && (uint)y < (uint)Height)
        {
            return y * Width + x;
        }

        if (Configuration.BoundaryMode == BoundaryMode.Wall)
        {
            return -1;
        }

        x %= Width;
        y %= Height;
        if (x < 0)
        {
            x += Width;
        }

        if (y < 0)
        {
            y += Height;
        }

        return y * Width + x;
    }

    internal int[] Lattice => _lattice;

    private void ValidateConfiguration()
    {
        if (!Enum.IsDefined(Configuration.BoundaryMode) ||
            !Enum.IsDefined(Configuration.Conventions.CopyNeighbourhood) ||
            !Enum.IsDefined(Configuration.Conventions.ContactCouplingNeighbourhood) ||
            !Enum.IsDefined(Configuration.Conventions.PerimeterNeighbourhood) ||
            !Enum.IsDefined(Configuration.Conventions.ConnectivityAdjacency))
        {
            throw new ArgumentOutOfRangeException(nameof(Configuration), "Configuration contains an unsupported boundary or stencil.");
        }
    }

    private void ValidatePeriodicStencils()
    {
        if (Configuration.BoundaryMode != BoundaryMode.Periodic)
        {
            return;
        }

        ValidatePeriodicStencil(StencilGeometry.Count(Configuration.Conventions.CopyNeighbourhood),
            (int index, out int x, out int y) => StencilGeometry.CopyOffset(Configuration.Conventions.CopyNeighbourhood, index, out x, out y));
        ValidatePeriodicStencil(StencilGeometry.Count(Configuration.Conventions.ContactCouplingNeighbourhood),
            (int index, out int x, out int y) => StencilGeometry.ContactOffset(Configuration.Conventions.ContactCouplingNeighbourhood, index, out x, out y));
        ValidatePeriodicStencil(StencilGeometry.Count(Configuration.Conventions.PerimeterNeighbourhood),
            (int index, out int x, out int y) => StencilGeometry.PerimeterOffset(Configuration.Conventions.PerimeterNeighbourhood, index, out x, out y));
        ValidatePeriodicStencil(StencilGeometry.Count(Configuration.Conventions.ConnectivityAdjacency),
            (int index, out int x, out int y) => StencilGeometry.ConnectivityOffset(Configuration.Conventions.ConnectivityAdjacency, index, out x, out y));
    }

    private void ValidatePeriodicStencil(int count, OffsetProvider getOffset)
    {
        HashSet<int> neighbours = [];
        for (int offsetIndex = 0; offsetIndex < count; offsetIndex++)
        {
            getOffset(offsetIndex, out int dx, out int dy);
            int x = dx % Width;
            int y = dy % Height;
            if (x < 0)
            {
                x += Width;
            }

            if (y < 0)
            {
                y += Height;
            }

            int index = y * Width + x;
            if (index == 0 || !neighbours.Add(index))
            {
                throw new ArgumentException("A periodic stencil aliases lattice sites for these dimensions.", nameof(Configuration));
            }
        }
    }

    private static void ValidateDefinition(CellDefinition definition, int typeCount)
    {
        if (definition.CellId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(definition), "Biological cell IDs must be positive.");
        }

        if ((uint)definition.CellTypeId >= (uint)typeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(definition), "Cell type IDs must index the contact matrix.");
        }

        if (!double.IsFinite(definition.TargetArea) || definition.TargetArea < 0 ||
            !double.IsFinite(definition.AreaStiffness) || definition.AreaStiffness < 0 ||
            !double.IsFinite(definition.TargetPerimeter) || definition.TargetPerimeter < 0 ||
            !double.IsFinite(definition.PerimeterStiffness) || definition.PerimeterStiffness < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(definition), "Targets and stiffnesses must be finite and non-negative.");
        }
    }

    private delegate void OffsetProvider(int index, out int x, out int y);
}
