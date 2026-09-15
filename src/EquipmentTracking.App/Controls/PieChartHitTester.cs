using System.Windows;

namespace EquipmentTracking.App.Controls;

public static class PieChartHitTester
{
    public const double DefaultPadding = 6d;

    public static int? FindSliceIndex(
        IReadOnlyList<int>? values,
        Size renderSize,
        Point point,
        double padding = DefaultPadding)
    {
        if (values is null ||
            values.Count == 0 ||
            !double.IsFinite(renderSize.Width) ||
            !double.IsFinite(renderSize.Height) ||
            !double.IsFinite(point.X) ||
            !double.IsFinite(point.Y) ||
            !double.IsFinite(padding) ||
            renderSize.Width <= 0 ||
            renderSize.Height <= 0 ||
            padding < 0)
        {
            return null;
        }

        var radius = Math.Min(renderSize.Width, renderSize.Height) / 2d - padding;
        if (radius <= 0)
        {
            return null;
        }

        var center = new Point(renderSize.Width / 2d, renderSize.Height / 2d);
        var deltaX = point.X - center.X;
        var deltaY = point.Y - center.Y;
        var distanceSquared = deltaX * deltaX + deltaY * deltaY;
        if (!double.IsFinite(distanceSquared) || distanceSquared > radius * radius)
        {
            return null;
        }

        var total = 0d;
        var firstPositiveIndex = -1;
        var lastPositiveIndex = -1;
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] <= 0)
            {
                continue;
            }

            firstPositiveIndex = firstPositiveIndex < 0 ? index : firstPositiveIndex;
            lastPositiveIndex = index;
            total += values[index];
        }

        if (firstPositiveIndex < 0 || total <= 0 || !double.IsFinite(total))
        {
            return null;
        }

        // The exact center has no meaningful angle. Resolve it predictably to
        // the first rendered slice instead of allowing floating-point noise to
        // choose a category.
        if (deltaX == 0 && deltaY == 0)
        {
            return firstPositiveIndex;
        }

        var angleFromTwelveClockwise =
            (Math.Atan2(deltaY, deltaX) * 180d / Math.PI + 450d) % 360d;
        var cumulativeAngle = 0d;
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] <= 0)
            {
                continue;
            }

            cumulativeAngle += 360d * values[index] / total;
            if (angleFromTwelveClockwise < cumulativeAngle || index == lastPositiveIndex)
            {
                return index;
            }
        }

        return lastPositiveIndex;
    }
}
