using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Rowles.Morphogenesis.Desktop.Configuration;
using Rowles.Morphogenesis.Desktop.Networking;
using Rowles.Morphogenesis.Desktop.ViewModels;
using Rowles.Morphogenesis.Desktop.Views;

namespace Rowles.Morphogenesis.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DesktopPreferences preferences = DesktopPreferences.LoadDefault();
            LaboratoryApiClient apiClient = new(new Uri(preferences.ServerBaseUri, UriKind.Absolute));
            desktop.MainWindow = new MainWindow(new MainWindowViewModel(apiClient, preferences));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
