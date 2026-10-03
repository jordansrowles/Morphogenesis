using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Rowles.Morphogenesis.Desktop.ViewModels;

namespace Rowles.Morphogenesis.Desktop.Views;

public sealed class MetricPlotControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<MetricSeries>> SeriesProperty =
        AvaloniaProperty.Register<MetricPlotControl, IReadOnlyList<MetricSeries>>(
            nameof(Series), Array.Empty<MetricSeries>());

    public IReadOnlyList<MetricSeries> Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SeriesProperty)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width < 24 || height < 24)
            return;

        Rect bounds = new(0, 0, width, height);
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(18, 26, 36)), null, bounds);
        double left = 8;
        double top = 8;
        double right = width - 8;
        double bottom = height - 8;
        Pen axis = new(new SolidColorBrush(Color.FromRgb(86, 102, 119)), 1);
        context.DrawLine(axis, new Point(left, top), new Point(left, bottom));
        context.DrawLine(axis, new Point(left, bottom), new Point(right, bottom));

        double minimumX = double.PositiveInfinity;
        double maximumX = double.NegativeInfinity;
        double minimumY = double.PositiveInfinity;
        double maximumY = double.NegativeInfinity;
        foreach (MetricSeries series in Series)
        {
            foreach (MetricPoint point in series.Points)
            {
                minimumX = Math.Min(minimumX, point.Mcs);
                maximumX = Math.Max(maximumX, point.Mcs);
                minimumY = Math.Min(minimumY, point.Value);
                maximumY = Math.Max(maximumY, point.Value);
            }
        }
        if (!double.IsFinite(minimumX) || !double.IsFinite(minimumY))
            return;
        if (maximumX == minimumX)
            maximumX = minimumX + 1;
        if (maximumY == minimumY)
        {
            minimumY -= 0.5;
            maximumY += 0.5;
        }

        foreach (MetricSeries series in Series)
        {
            if (series.Points.Count < 2)
                continue;
            Pen line = new(new SolidColorBrush(Color.FromRgb(series.Colour.Red, series.Colour.Green, series.Colour.Blue)), 1.6);
            MetricPoint previous = series.Points[0];
            for (int index = 1; index < series.Points.Count; index++)
            {
                MetricPoint current = series.Points[index];
                Point start = Map(previous, left, top, right, bottom, minimumX, maximumX, minimumY, maximumY);
                Point end = Map(current, left, top, right, bottom, minimumX, maximumX, minimumY, maximumY);
                context.DrawLine(line, start, end);
                previous = current;
            }
        }
    }

    private static Point Map(MetricPoint point, double left, double top, double right, double bottom,
        double minimumX, double maximumX, double minimumY, double maximumY)
    {
        double x = left + (point.Mcs - minimumX) / (maximumX - minimumX) * (right - left);
        double y = bottom - (point.Value - minimumY) / (maximumY - minimumY) * (bottom - top);
        return new Point(x, y);
    }
}
