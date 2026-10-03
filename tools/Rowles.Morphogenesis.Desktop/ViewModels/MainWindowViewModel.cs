using System.Net;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rowles.Morphogenesis.Desktop.Configuration;
using Rowles.Morphogenesis.Desktop.Networking;

namespace Rowles.Morphogenesis.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ILaboratoryApiClient _apiClient;
    private DesktopPreferences _preferences;
    private readonly ILiveFrameStreamClient _streamClient;

    public MainWindowViewModel(
        ILaboratoryApiClient apiClient,
        DesktopPreferences preferences,
        ILiveFrameStreamClient? streamClient = null)
    {
        _apiClient = apiClient;
        _preferences = preferences;
        _streamClient = streamClient ?? new LiveFrameStreamClient();
        Experiments = new ExperimentListViewModel(apiClient, OpenSession);
        Sessions = new SessionsListViewModel(apiClient, OpenSession);
        _serverBaseUri = preferences.ServerBaseUri;
        _networkWarning = GetNetworkWarning(_serverBaseUri);
        _currentContent = Experiments;
    }

    public ExperimentListViewModel Experiments { get; }
    public SessionsListViewModel Sessions { get; }

    [ObservableProperty]
    private string _serverBaseUri;

    [ObservableProperty]
    private string _networkWarning;

    [ObservableProperty]
    private object _currentContent;

    [ObservableProperty]
    private SessionViewModel? _activeSession;

    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        await Experiments.LoadAsync(cancellationToken);
        await Sessions.LoadAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task ShowExperimentsAsync()
    {
        if (ActiveSession is not null)
        {
            await ActiveSession.DisposeAsync();
            ActiveSession = null;
        }
        CurrentContent = Experiments;
    }

    [RelayCommand]
    private async Task ShowSessionsAsync()
    {
        if (ActiveSession is not null)
        {
            await ActiveSession.DisposeAsync();
            ActiveSession = null;
        }
        await Sessions.LoadAsync();
        CurrentContent = Sessions;
    }

    public async ValueTask DisposeAsync()
    {
        if (ActiveSession is not null)
            await ActiveSession.DisposeAsync();
        _apiClient.Dispose();
    }

    partial void OnServerBaseUriChanged(string value)
    {
        NetworkWarning = GetNetworkWarning(value);
        if (!DesktopPreferences.IsHttpUri(value) || !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            return;
        _apiClient.SetBaseAddress(uri);
        _preferences = new DesktopPreferences(uri.AbsoluteUri.TrimEnd('/'));
        try
        {
            _preferences.SaveDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task OpenSession(SessionDto session)
    {
        if (ActiveSession is not null)
            await ActiveSession.DisposeAsync();
        ActiveSession = new SessionViewModel(session, _apiClient, _streamClient);
        CurrentContent = ActiveSession;
        await ActiveSession.OpenAsync();
    }

    private static string GetNetworkWarning(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme != "http")
            return string.Empty;
        bool loopback = string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                        IPAddress.TryParse(uri.Host, out IPAddress? address) && IPAddress.IsLoopback(address);
        return loopback ? string.Empty : "This server has no authentication. Use only on a trusted network.";
    }
}
