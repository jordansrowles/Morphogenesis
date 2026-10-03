using Rowles.Morphogenesis.Laboratory.Sessions;

namespace Rowles.Morphogenesis.Server.Configuration;

public sealed class SessionCapacityService
{
    private readonly object _gate = new();
    private readonly LaboratoryResourceLimits _limits;
    private readonly HashSet<Guid> _runningReservations = [];
    private readonly HashSet<Guid> _stepReservations = [];
    private readonly Dictionary<Guid, SimulationSessionStatus> _observedStatuses = [];

    public SessionCapacityService(LaboratoryResourceLimits limits)
    {
        _limits = limits;
    }

    public bool TryReserveRunning(Guid sessionId)
    {
        lock (_gate)
        {
            if (_runningReservations.Contains(sessionId))
                return true;
            if (_runningReservations.Count + _stepReservations.Count >= _limits.MaxRunningSessions)
                return false;
            _runningReservations.Add(sessionId);
            return true;
        }
    }

    public bool TryReserveStep(Guid sessionId)
    {
        lock (_gate)
        {
            if (_runningReservations.Count + _stepReservations.Count >= _limits.MaxRunningSessions)
                return false;
            _stepReservations.Add(sessionId);
            return true;
        }
    }

    public void CompleteStep(Guid sessionId)
    {
        lock (_gate)
            _stepReservations.Remove(sessionId);
    }

    public void ReleaseRunning(Guid sessionId)
    {
        lock (_gate)
            _runningReservations.Remove(sessionId);
    }

    public void ObserveStatus(Guid sessionId, SimulationSessionStatus status)
    {
        lock (_gate)
        {
            bool hadPrevious = _observedStatuses.TryGetValue(sessionId, out SimulationSessionStatus previous);
            _observedStatuses[sessionId] = status;
            if (status == SimulationSessionStatus.Running)
                _runningReservations.Add(sessionId);
            else if (hadPrevious && previous == SimulationSessionStatus.Running)
                _runningReservations.Remove(sessionId);
        }
    }

    public void RemoveSession(Guid sessionId)
    {
        lock (_gate)
        {
            _runningReservations.Remove(sessionId);
            _stepReservations.Remove(sessionId);
            _observedStatuses.Remove(sessionId);
        }
    }
}
