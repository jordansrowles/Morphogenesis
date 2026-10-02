using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Experiments.Execution;
using Rowles.Morphogenesis.Laboratory.Recording;

namespace Rowles.Morphogenesis.Laboratory.Sessions;

public static class SimulationSessionFactory
{
    public static SimulationSession Create(
        ExperimentManifest manifest,
        int replicateIndex,
        SimulationSessionOptions? options = null,
        RecordingOptions? recordingOptions = null,
        IRecordingStore? recordingStore = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();
        SimulationSessionOptions resolvedOptions = options ?? new SimulationSessionOptions();
        resolvedOptions.Validate();
        RecordingOptions resolvedRecordingOptions = recordingOptions ?? new RecordingOptions();
        resolvedRecordingOptions.Validate();
        if (resolvedRecordingOptions.Enabled && recordingStore is null)
        {
            throw new ArgumentNullException(nameof(recordingStore), "An enabled recording requires a recording store.");
        }

        if (!resolvedRecordingOptions.Enabled && recordingStore is not null)
        {
            throw new ArgumentException("A recording store can only be supplied when recording is enabled.", nameof(recordingStore));
        }

        CoalescingLatticeChangeAccumulator? accumulator = resolvedRecordingOptions.Enabled
            ? new CoalescingLatticeChangeAccumulator(checked(manifest.GridWidth * manifest.GridHeight))
            : null;
        ExperimentSimulationInstance instance = ExperimentSimulationFactory.Create(manifest, replicateIndex, accumulator);
        return new SimulationSession(
            Guid.NewGuid(),
            manifest,
            instance,
            resolvedOptions,
            recordingOptions: resolvedRecordingOptions,
            recordingAccumulator: accumulator,
            recordingStore: recordingStore);
    }
}
