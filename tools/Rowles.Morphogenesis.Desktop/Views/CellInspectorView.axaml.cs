using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Rowles.Morphogenesis.Desktop.Views;

public sealed partial class CellInspectorView : UserControl
{
    public CellInspectorView() => AvaloniaXamlLoader.Load(this);
}
