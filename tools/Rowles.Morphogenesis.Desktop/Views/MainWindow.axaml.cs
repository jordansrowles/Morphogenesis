using Avalonia.Controls;
using Rowles.Morphogenesis.Desktop.ViewModels;

namespace Rowles.Morphogenesis.Desktop.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitialiseAsync();
        Closing += async (_, _) => await viewModel.DisposeAsync();
    }

    private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
}
