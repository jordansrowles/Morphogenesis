using Rowles.Morphogenesis.Experiments.Execution;
using Rowles.Morphogenesis.Experiments;
using Rowles.Morphogenesis.Dynamics;
using Rowles.Morphogenesis.Laboratory.Sessions;
using Rowles.Morphogenesis.Tests.Laboratory;

namespace Rowles.Morphogenesis.Tests.Laboratory.Sessions;

public sealed class SimulationSessionTests
{
    [Fact]
    public async Task ConstructionPublishesInitialStateMeasurementAndDetachedMetadata()
    {
        ExperimentManifest manifest = SessionTestFixture.CreateManifest();
        ExperimentSimulationInstance expected = ExperimentSimulationFactory.Create(manifest, 0);
        await using SimulationSession session = SimulationSessionFactory.Create(manifest, replicateIndex: 0);

        SimulationSessionSnapshot snapshot = session.GetSnapshot();
        Assert.Equal(session.Metadata.SessionId, snapshot.SessionId);
        Assert.Equal(0, snapshot.Revision);
        Assert.Equal(SimulationSessionStatus.Created, snapshot.Status);
        Assert.Equal(0, snapshot.CurrentMcs);
        Assert.Equal(0, snapshot.LatestMeasurement!.Mcs);
        Assert.Equal(0, session.LatestMeasurement!.Mcs);
        Assert.Equal(expected.ExperimentId, session.Metadata.RunIdentity.ExperimentId);
        Assert.Equal(expected.ReplicateId, session.Metadata.RunIdentity.ReplicateId);
        Assert.Equal(expected.ReplicateIndex, session.Metadata.RunIdentity.ReplicateIndex);
        Assert.Equal(expected.ReplicateSeed, session.Metadata.RunIdentity.ReplicateSeed);
        Assert.Equal(expected.InitialisationSeed, session.Metadata.RunIdentity.InitialisationSeed);
        Assert.Equal(expected.DynamicsSeed, session.Metadata.RunIdentity.DynamicsSeed);
        Assert.Equal(SerialSimulation.KernelId, session.Metadata.RunIdentity.KernelId);

        int[] expectedCellTypeById = new int[expected.Simulation.State.CellCapacity];
        Array.Fill(expectedCellTypeById, -1);
        expectedCellTypeById[0] = 0;
        for (int cellId = 1; cellId < expectedCellTypeById.Length; cellId++)
        {
            if (expected.Simulation.State.TryGetCellState(cellId, out Rowles.Morphogenesis.Model.CellState cell))
            {
                expectedCellTypeById[cellId] = cell.CellTypeId;
            }
        }

        Assert.Equal(expectedCellTypeById, session.Metadata.CellTypeByCellId);

        var cellTypes = session.Metadata.CellTypes;
        int[] cellTypeById = session.Metadata.CellTypeByCellId;
        cellTypes[0] = cellTypes[^1] with { Name = "Changed" };
        int liveCellId = Array.FindIndex(cellTypeById, cellType => cellType > 0);
        Assert.True(liveCellId > 0);
        cellTypeById[liveCellId] = int.MaxValue;
        Assert.NotEqual("Changed", session.Metadata.CellTypes[0].Name);
        Assert.NotEqual(int.MaxValue, session.Metadata.CellTypeByCellId[liveCellId]);

        await using IAsyncEnumerator<SimulationSessionSnapshot> states = session.WatchStateAsync().GetAsyncEnumerator();
        Assert.True(await states.MoveNextAsync());
        Assert.Equal(SimulationSessionStatus.Created, states.Current.Status);

        await using IAsyncEnumerator<Rowles.Morphogenesis.Experiments.Results.MeasurementSample> measurements =
            session.WatchMeasurementsAsync().GetAsyncEnumerator();
        Assert.True(await measurements.MoveNextAsync());
        Assert.Equal(0, measurements.Current.Mcs);
    }

    [Fact]
    public void OptionsKeepTheResourceLimitsFixedAndBoundThePublicationRate()
    {
        Assert.Equal(10, new SimulationSessionOptions().LivePublishMaxFps);
        Assert.Throws<ArgumentOutOfRangeException>(() => SimulationSessionFactory.Create(
            SessionTestFixture.CreateManifest(), 0, new SimulationSessionOptions { LivePublishMaxFps = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => SimulationSessionFactory.Create(
            SessionTestFixture.CreateManifest(), 0, new SimulationSessionOptions { LivePublishMaxFps = 21 }));
        Assert.Throws<ArgumentException>(() => SimulationSessionFactory.Create(
            SessionTestFixture.CreateManifest(), 0, new SimulationSessionOptions { CommandQueueCapacity = 63 }));
        Assert.Throws<ArgumentException>(() => SimulationSessionFactory.Create(
            SessionTestFixture.CreateManifest(), 0, new SimulationSessionOptions { MaxLiveSubscribers = 7 }));
        Assert.Throws<ArgumentException>(() => SimulationSessionFactory.Create(
            SessionTestFixture.CreateManifest(), 0, new SimulationSessionOptions { FrameBufferCount = 9 }));
    }

    [Fact]
    public async Task CreatedAndPausedStepsAdvanceOneMcsAndCompleteWithOneRevisionEach()
    {
        await using SimulationSession session = SimulationSessionFactory.Create(
            SessionTestFixture.CreateManifest(mcsCount: 3), replicateIndex: 0);

        SimulationCommandResult first = await session.StepAsync(Guid.NewGuid(), expectedRevision: 0);
        AssertApplied(first, revision: 1, status: SimulationSessionStatus.Paused, mcs: 1);

        SimulationCommandResult second = await session.StepAsync(Guid.NewGuid(), expectedRevision: 1);
        AssertApplied(second, revision: 2, status: SimulationSessionStatus.Paused, mcs: 2);

        SimulationCommandResult final = await session.StepAsync(Guid.NewGuid(), expectedRevision: 2);
        AssertApplied(final, revision: 3, status: SimulationSessionStatus.Completed, mcs: 3);

        SimulationCommandResult terminal = await session.StepAsync(Guid.NewGuid(), expectedRevision: 3);
        Assert.Equal(SimulationCommandDisposition.Terminal, terminal.Disposition);
        Assert.Equal(3, terminal.Revision);
    }

    [Fact]
    public async Task CreatedStopCancelsAndTerminalCommandsDoNotChangeRevision()
    {
        await using SimulationSession session = SimulationSessionFactory.Create(SessionTestFixture.CreateManifest(), replicateIndex: 0);

        SimulationCommandResult stopped = await session.StopAsync(Guid.NewGuid(), expectedRevision: 0);
        AssertApplied(stopped, revision: 1, status: SimulationSessionStatus.Cancelled, mcs: 0);

        SimulationCommandResult terminal = await session.StartAsync(Guid.NewGuid(), expectedRevision: 1);
        Assert.Equal(SimulationCommandDisposition.Terminal, terminal.Disposition);
        Assert.Equal(1, terminal.Revision);
        Assert.Equal(SimulationSessionStatus.Cancelled, terminal.Status);
    }

    [Fact]
    public async Task LifecycleTablePausesAndStopsOnlyAtMcsBoundaries()
    {
        await using SimulationSession session = SimulationSessionFactory.Create(
            SessionTestFixture.CreateManifest(mcsCount: 1_000_000), replicateIndex: 0);

        Assert.Equal(SimulationCommandDisposition.InvalidState,
            (await session.PauseAsync(Guid.NewGuid(), 0)).Disposition);
        Assert.Equal(SimulationCommandDisposition.InvalidState,
            (await session.ResumeAsync(Guid.NewGuid(), 0)).Disposition);

        SimulationCommandResult started = await session.StartAsync(Guid.NewGuid(), 0);
        AssertApplied(started, revision: 1, status: SimulationSessionStatus.Running, mcs: 0);
        Assert.Equal(SimulationCommandDisposition.InvalidState,
            (await session.StartAsync(Guid.NewGuid(), 1)).Disposition);
        Assert.Equal(SimulationCommandDisposition.InvalidState,
            (await session.StepAsync(Guid.NewGuid(), 1)).Disposition);

        SimulationCommandResult paused = await session.PauseAsync(Guid.NewGuid(), 1);
        Assert.Equal(SimulationCommandDisposition.Applied, paused.Disposition);
        Assert.Equal(2, paused.Revision);
        Assert.Equal(SimulationSessionStatus.Paused, paused.Status);
        Assert.True(paused.CurrentMcs > 0);

        Assert.Equal(SimulationCommandDisposition.InvalidState,
            (await session.StartAsync(Guid.NewGuid(), 2)).Disposition);
        Assert.Equal(SimulationCommandDisposition.InvalidState,
            (await session.PauseAsync(Guid.NewGuid(), 2)).Disposition);

        SimulationCommandResult resumed = await session.ResumeAsync(Guid.NewGuid(), 2);
        AssertApplied(resumed, revision: 3, status: SimulationSessionStatus.Running, mcs: resumed.CurrentMcs);

        SimulationCommandResult stopped = await session.StopAsync(Guid.NewGuid(), 3);
        Assert.Equal(SimulationCommandDisposition.Applied, stopped.Disposition);
        Assert.Equal(4, stopped.Revision);
        Assert.Equal(SimulationSessionStatus.Cancelled, stopped.Status);
        Assert.True(stopped.CurrentMcs >= resumed.CurrentMcs);
    }

    [Fact]
    public async Task StaleExpectedRevisionConflictsWithoutApplyingTheStep()
    {
        await using SimulationSession session = SimulationSessionFactory.Create(
            SessionTestFixture.CreateManifest(mcsCount: 10), replicateIndex: 0);
        SimulationCommandResult firstStep = await session.StepAsync(Guid.NewGuid(), 0);

        SimulationCommandResult stale = await session.StepAsync(Guid.NewGuid(), expectedRevision: 0);

        Assert.Equal(SimulationCommandDisposition.Conflict, stale.Disposition);
        Assert.Equal(0, stale.ExpectedRevision);
        Assert.Equal(1, stale.Revision);
        Assert.Equal(firstStep.CurrentMcs, stale.CurrentMcs);
        Assert.Equal(SimulationSessionStatus.Paused, stale.Status);
    }

    [Fact]
    public async Task CommandsWithTheSameExpectedRevisionAreAppliedInFifoOrder()
    {
        await using SimulationSession session = SimulationSessionFactory.Create(
            SessionTestFixture.CreateManifest(mcsCount: 1_000_000), replicateIndex: 0);

        ValueTask<SimulationCommandResult> firstCommand = session.StartAsync(Guid.NewGuid(), expectedRevision: 0);
        ValueTask<SimulationCommandResult> secondCommand = session.StepAsync(Guid.NewGuid(), expectedRevision: 0);
        SimulationCommandResult[] results = await Task.WhenAll(firstCommand.AsTask(), secondCommand.AsTask());

        Assert.Equal(SimulationCommandDisposition.Applied, results[0].Disposition);
        Assert.Equal(SimulationCommandDisposition.Conflict, results[1].Disposition);
        Assert.Equal(1, results[0].Revision);
        Assert.Equal(1, results[1].Revision);

        await session.StopAsync(Guid.NewGuid(), expectedRevision: 1);
    }

    [Fact]
    public async Task CancelledQueuedCommandDoesNotChangeSessionState()
    {
        await using SimulationSession session = SimulationSessionFactory.Create(
            SessionTestFixture.CreateManifest(mcsCount: 1_000_000, width: 256, height: 256), replicateIndex: 0);
        await session.StartAsync(Guid.NewGuid(), expectedRevision: 0);
        using CancellationTokenSource cancellation = new();

        ValueTask<SimulationCommandResult> pendingPause = session.PauseAsync(
            Guid.NewGuid(), expectedRevision: 1, cancellationToken: cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingPause.AsTask());
        SimulationSessionSnapshot afterCancellation = session.GetSnapshot();
        Assert.Equal(1, afterCancellation.Revision);
        Assert.Equal(SimulationSessionStatus.Running, afterCancellation.Status);

        await session.StopAsync(Guid.NewGuid(), expectedRevision: 1);
    }

    [Fact]
    public async Task NormalCompletionAdvancesTheTerminalRevisionOnce()
    {
        await using SimulationSession session = SimulationSessionFactory.Create(
            SessionTestFixture.CreateManifest(mcsCount: 1), replicateIndex: 0);
        SimulationCommandResult started = await session.StartAsync(Guid.NewGuid(), expectedRevision: 0);

        AssertApplied(started, revision: 1, status: SimulationSessionStatus.Running, mcs: 0);
        SimulationSessionSnapshot completed = await SessionTestFixture.WaitForStatusAsync(session, SimulationSessionStatus.Completed);

        Assert.Equal(2, completed.Revision);
        Assert.Equal(1, completed.CurrentMcs);
        Assert.Equal(2, session.GetSnapshot().Revision);
    }

    [Fact]
    public async Task InspectionReturnsDetachedLiveCellDataWithoutChangingRevision()
    {
        ExperimentManifest manifest = SessionTestFixture.CreateManifest(mcsCount: 10);
        ExperimentSimulationInstance expected = ExperimentSimulationFactory.Create(manifest, 0);
        int liveCellId = Enumerable.Range(1, expected.Simulation.State.CellCapacity - 1)
            .First(cellId => expected.Simulation.State.TryGetCellState(cellId, out _));
        expected.Simulation.State.TryGetCellState(liveCellId, out Rowles.Morphogenesis.Model.CellState expectedCell);
        await using SimulationSession session = SimulationSessionFactory.Create(manifest, replicateIndex: 0);

        CellInspection? inspection = await session.InspectCellAsync(liveCellId);
        CellInspection? medium = await session.InspectCellAsync(0);
        CellInspection? unknown = await session.InspectCellAsync(int.MaxValue);

        Assert.NotNull(inspection);
        Assert.Equal(0, inspection.Mcs);
        Assert.Equal(expectedCell.CellId, inspection.CellId);
        Assert.Equal(expectedCell.CellTypeId, inspection.CellTypeId);
        Assert.Equal(expectedCell.Area, inspection.Area);
        Assert.Equal(expectedCell.Perimeter, inspection.Perimeter);
        Assert.Null(medium);
        Assert.Null(unknown);
        Assert.Equal(0, session.GetSnapshot().Revision);
        Assert.Equal(SimulationSessionStatus.Created, session.GetSnapshot().Status);
    }

    [Fact]
    public async Task DisposalDoesNotRewriteTheSessionAsCancelled()
    {
        SimulationSession session = SimulationSessionFactory.Create(
            SessionTestFixture.CreateManifest(mcsCount: 1_000_000), replicateIndex: 0);
        await session.StartAsync(Guid.NewGuid(), expectedRevision: 0);

        await session.DisposeAsync();

        Assert.Equal(SimulationSessionStatus.Running, session.GetSnapshot().Status);
        Assert.Equal(1, session.GetSnapshot().Revision);
    }

    private static void AssertApplied(
        SimulationCommandResult result,
        long revision,
        SimulationSessionStatus status,
        long mcs)
    {
        Assert.Equal(SimulationCommandDisposition.Applied, result.Disposition);
        Assert.Equal(revision, result.Revision);
        Assert.Equal(status, result.Status);
        Assert.Equal(mcs, result.CurrentMcs);
    }
}
