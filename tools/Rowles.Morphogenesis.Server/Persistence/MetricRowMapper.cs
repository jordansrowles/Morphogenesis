using System.Globalization;
using Rowles.Morphogenesis.Experiments.Results;

namespace Rowles.Morphogenesis.Server.Persistence;

public readonly record struct MetricRow(string Metric, double Value);

public static class MetricRowMapper
{
    public static PersistedMetricSample ToSample(MeasurementSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return new PersistedMetricSample(
            sample.Mcs,
            ToRows(sample).ToDictionary(row => row.Metric, row => row.Value, StringComparer.Ordinal));
    }

    public static IReadOnlyList<MetricRow> ToRows(MeasurementSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        List<MetricRow> rows =
        [
            new("heterotypic-interface-count", sample.Metrics.HeterotypicInterfaceCount),
            new("total-cell-cell-interface-count", sample.Metrics.TotalCellCellInterfaceCount),
            new("heterotypic-interface-fraction", sample.Metrics.HeterotypicInterfaceFraction),
            new("type-a-cell-count", sample.Metrics.TypeACellCount),
            new("type-b-cell-count", sample.Metrics.TypeBCellCount),
            new("cell-area.mean", sample.Metrics.MeanCellArea),
            new("cell-area.min", sample.Metrics.MinimumCellArea),
            new("cell-area.max", sample.Metrics.MaximumCellArea),
            new("cell-perimeter.mean", sample.Metrics.MeanCellPerimeter),
            new("cell-perimeter.min", sample.Metrics.MinimumCellPerimeter),
            new("cell-perimeter.max", sample.Metrics.MaximumCellPerimeter)
        ];

        foreach (var item in sample.Metrics.HomotypicInterfacesByType.OrderBy(item => item.TypeId))
        {
            rows.Add(new MetricRow(
                $"homotypic-interface-count.type-{item.TypeId.ToString(CultureInfo.InvariantCulture)}",
                item.Count));
        }

        foreach (var item in sample.Metrics.DomainsByType.OrderBy(item => item.TypeId))
        {
            string typeId = item.TypeId.ToString(CultureInfo.InvariantCulture);
            rows.Add(new MetricRow($"type-domain-count.type-{typeId}", item.DomainCount));
            rows.Add(new MetricRow($"largest-domain-size.type-{typeId}", item.LargestDomainCellCount));
        }

        return rows;
    }
}
