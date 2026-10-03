using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Rowles.Morphogenesis.Desktop.ViewModels;

namespace Rowles.Morphogenesis.Desktop.Views;

public sealed partial class SessionView : UserControl
{
    private Border _latticeViewport = null!;
    private Image _latticeImage = null!;

    public SessionView()
    {
        AvaloniaXamlLoader.Load(this);
        _latticeViewport = this.FindControl<Border>("LatticeViewport")!;
        _latticeImage = this.FindControl<Image>("LatticeImage")!;
        RenderOptions.SetBitmapInterpolationMode(_latticeImage, BitmapInterpolationMode.None);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_latticeViewport).Properties.IsLeftButtonPressed || DataContext is not SessionViewModel viewModel)
            return;
        viewModel.BeginPointer(e.GetPosition(_latticeViewport));
        e.Pointer.Capture(_latticeViewport);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is SessionViewModel viewModel)
            viewModel.MovePointer(e.GetPosition(_latticeViewport));
    }

    private async void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not SessionViewModel viewModel)
            return;
        e.Pointer.Capture(null);
        await viewModel.EndPointerAsync(e.GetPosition(_latticeViewport), _latticeViewport.Bounds.Width,
            _latticeViewport.Bounds.Height);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is SessionViewModel viewModel)
        {
            viewModel.ZoomAt(e.GetPosition(_latticeViewport), e.Delta.Y,
                _latticeViewport.Bounds.Width, _latticeViewport.Bounds.Height);
            e.Handled = true;
        }
    }
}
