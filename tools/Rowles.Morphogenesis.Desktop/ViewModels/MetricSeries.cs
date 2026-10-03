using Rowles.Morphogenesis.Desktop.Rendering;

namespace Rowles.Morphogenesis.Desktop.ViewModels;

public sealed record MetricPoint(double Mcs, double Value);

public sealed record MetricSeries(string Name, RgbColour Colour, IReadOnlyList<MetricPoint> Points);
