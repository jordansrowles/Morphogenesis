using System.Text;
using Rowles.Morphogenesis.Reference.Energy;
using Rowles.Morphogenesis.Reference.Lattice;
using Rowles.Morphogenesis.Reference.Model;
using Rowles.Morphogenesis.Reference.Topology;
using Rowles.Morphogenesis.Reference.Dynamics;

namespace Rowles.Morphogenesis.Reference.Diagnostics;

public static class ReferenceDiagnostics
{
    public static string FormatState(ReferenceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        StringBuilder output = new();
        AppendLattice(output, state);
        AppendTypes(output, state);
        HamiltonianBreakdown energy = ReferenceEnergy.ComputeHamiltonian(state);
        output.AppendLine($"Hamiltonian: contact={energy.Contact:R}, area={energy.Area:R}, perimeter={energy.Perimeter:R}, total={energy.Total:R}");
        return output.ToString();
    }

    public static string FormatMove(
        ReferenceState state,
        GridPoint source,
        GridPoint target,
        double fluctuationAmplitude,
        double? acceptanceRandomValue = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        BruteForceMove full = BruteForceMoveReference.Evaluate(state, source, target);
        MoveDelta local = LocalMoveDelta.Evaluate(state, source, target);
        double probability = ReferenceSimulation.AcceptanceProbability(local.Total, fluctuationAmplitude);
        ProposedConnectivityCheck topology = ConnectivityReference.EvaluateCopy(state, source, target);
        StringBuilder output = new();
        output.AppendLine(FormatState(state).TrimEnd());
        output.AppendLine($"Proposed copy: source={source} id={full.NewCellId}, target={target} old={full.OldCellId}");
        output.AppendLine($"Local DeltaH: contact={local.Terms.Contact:R}, area={local.Terms.Area:R}, perimeter={local.Terms.Perimeter:R}, total={local.Total:R}");
        output.AppendLine($"Full before: contact={full.Before.Contact:R}, area={full.Before.Area:R}, perimeter={full.Before.Perimeter:R}, total={full.Before.Total:R}");
        output.AppendLine($"Full after: contact={full.After.Contact:R}, area={full.After.Area:R}, perimeter={full.After.Perimeter:R}, total={full.After.Total:R}");
        output.AppendLine($"Acceptance: T={fluctuationAmplitude:R}, probability={probability:R}, draw={(acceptanceRandomValue?.ToString("R") ?? "not consumed")}");
        output.AppendLine(topology.LosingCellId == 0
            ? "Connectivity: target was medium; there is no losing biological cell."
            : $"Connectivity: losing cell {topology.LosingCellId}, components after copy={topology.ComponentsAfter}, remains connected={topology.WouldRemainConnected}");
        return output.ToString();
    }

    private static void AppendLattice(StringBuilder output, ReferenceState state)
    {
        output.AppendLine($"Lattice {state.Width}x{state.Height} (top row first):");
        for (int y = state.Height - 1; y >= 0; y--)
        {
            for (int x = 0; x < state.Width; x++)
            {
                if (x > 0)
                {
                    output.Append(' ');
                }

                output.Append(state.CellIdAt(x, y));
            }

            output.AppendLine();
        }
    }

    private static void AppendTypes(StringBuilder output, ReferenceState state)
    {
        output.Append("Cell types:");
        foreach (CellTypeDefinition type in state.CellTypes)
        {
            output.Append($" {type.TypeId}={type.Name}");
        }

        output.AppendLine();
    }
}
