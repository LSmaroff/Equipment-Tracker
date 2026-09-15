using System.Windows.Media;

namespace EquipmentTracking.App.Models;

public sealed class PieChartSlice
{
    public string Label { get; init; } = string.Empty;
    public int Value { get; init; }
    public double Percentage { get; init; }
    public Brush Brush { get; init; } = Brushes.SlateGray;
    public string DisplayText => $"{Label} — {Value} ({Percentage:0.#}%)";
}
