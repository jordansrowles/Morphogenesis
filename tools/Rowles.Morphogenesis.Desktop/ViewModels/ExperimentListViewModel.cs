using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rowles.Morphogenesis.Desktop.Networking;

namespace Rowles.Morphogenesis.Desktop.ViewModels;

public sealed partial class ExperimentListViewModel : ObservableObject
{
    private readonly ILaboratoryApiClient _apiClient;
    private readonly Func<SessionDto, Task> _openSession;

    public ExperimentListViewModel(ILaboratoryApiClient apiClient, Func<SessionDto, Task> openSession)
    {
        _apiClient = apiClient;
        _openSession = openSession;
    }

    public ObservableCollection<ExperimentSummaryDto> Experiments { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateSessionCommand))]
    private ExperimentSummaryDto? _selectedExperiment;

    [ObservableProperty]
    private int _replicateIndex;

    [ObservableProperty]
    private bool _recordingEnabled = true;

    [ObservableProperty]
    private int _liveFps = 10;

    [ObservableProperty]
    private string _status = "Loading experiments…";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<ExperimentSummaryDto> experiments = await _apiClient.GetExperimentsAsync(cancellationToken);
            Experiments.Clear();
            foreach (ExperimentSummaryDto experiment in experiments)
                Experiments.Add(experiment);
            SelectedExperiment ??= Experiments.FirstOrDefault();
            Status = Experiments.Count == 0 ? "The server has no experiments." : "Choose an experiment and replicate.";
        }
        catch (Exception exception) when (exception is LaboratoryApiException or HttpRequestException or TaskCanceledException)
        {
            Status = exception.Message;
        }
    }

    private bool CanCreateSession() => SelectedExperiment is not null && ReplicateIndex >= 0 && LiveFps is >= 1 and <= 60;

    [RelayCommand(CanExecute = nameof(CanCreateSession))]
    private async Task CreateSessionAsync(CancellationToken cancellationToken)
    {
        ExperimentSummaryDto? experiment = SelectedExperiment;
        if (experiment is null)
            return;
        try
        {
            SessionDto session = await _apiClient.CreateSessionAsync(
                new CreateSessionRequestDto(experiment.ExperimentId, ReplicateIndex, RecordingEnabled, LiveFps),
                cancellationToken);
            Status = $"Created session {session.SessionId:D}.";
            await _openSession(session);
        }
        catch (Exception exception) when (exception is LaboratoryApiException or HttpRequestException or TaskCanceledException)
        {
            Status = exception.Message;
        }
    }

    partial void OnReplicateIndexChanged(int value) => CreateSessionCommand.NotifyCanExecuteChanged();
    partial void OnLiveFpsChanged(int value) => CreateSessionCommand.NotifyCanExecuteChanged();
}
