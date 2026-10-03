using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rowles.Morphogenesis.Desktop.Networking;

namespace Rowles.Morphogenesis.Desktop.ViewModels;

public sealed partial class SessionsListViewModel : ObservableObject
{
    private readonly ILaboratoryApiClient _apiClient;
    private readonly Func<SessionDto, Task> _openSession;

    public SessionsListViewModel(ILaboratoryApiClient apiClient, Func<SessionDto, Task> openSession)
    {
        _apiClient = apiClient;
        _openSession = openSession;
    }

    public ObservableCollection<SessionDto> Sessions { get; } = [];

    [ObservableProperty]
    private string _status = "Load a session from the server.";

    [ObservableProperty]
    private SessionDto? _selectedSession;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<SessionDto> sessions = await _apiClient.GetSessionsAsync(cancellationToken);
            Sessions.Clear();
            foreach (SessionDto session in sessions)
                Sessions.Add(session);
            Status = $"{Sessions.Count} session(s).";
        }
        catch (Exception exception) when (exception is LaboratoryApiException or HttpRequestException or TaskCanceledException)
        {
            Status = exception.Message;
        }
    }

    [RelayCommand]
    private async Task OpenSessionAsync(CancellationToken cancellationToken)
    {
        SessionDto? selected = SelectedSession;
        if (selected is null)
            return;
        try
        {
            SessionDto authoritative = await _apiClient.GetSessionAsync(selected.SessionId, cancellationToken);
            await _openSession(authoritative);
        }
        catch (Exception exception) when (exception is LaboratoryApiException or HttpRequestException or TaskCanceledException)
        {
            Status = exception.Message;
        }
    }
}
