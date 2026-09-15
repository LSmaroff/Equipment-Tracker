using System.Windows;
using EquipmentTracking.App.Controls;

namespace EquipmentTracking.Tests;

public sealed class PieChartHitTesterTests
{
    private static readonly Size SquareChart = new(200, 200);

    [Fact]
    public void FindSliceIndex_MapsClockwiseQuarterSlicesFromTwelveOClock()
    {
        int[] values = [1, 1, 1, 1];

        Assert.Equal(0, PieChartHitTester.FindSliceIndex(values, SquareChart, new Point(100, 20)));
        Assert.Equal(1, PieChartHitTester.FindSliceIndex(values, SquareChart, new Point(180, 100)));
        Assert.Equal(2, PieChartHitTester.FindSliceIndex(values, SquareChart, new Point(100, 180)));
        Assert.Equal(3, PieChartHitTester.FindSliceIndex(values, SquareChart, new Point(20, 100)));
    }

    [Fact]
    public void FindSliceIndex_UsesHalfOpenBoundariesForWeightedSlices()
    {
        int[] values = [1, 3];

        Assert.Equal(0, PieChartHitTester.FindSliceIndex(values, SquareChart, new Point(100, 20)));
        Assert.Equal(1, PieChartHitTester.FindSliceIndex(values, SquareChart, new Point(180, 100)));
        Assert.Equal(1, PieChartHitTester.FindSliceIndex(values, SquareChart, new Point(100, 180)));
    }

    [Fact]
    public void FindSliceIndex_IgnoresNonPositiveValuesAndReturnsOriginalIndex()
    {
        int[] values = [0, 2, -4, 3];

        Assert.Equal(1, PieChartHitTester.FindSliceIndex(values, SquareChart, new Point(100, 20)));
        Assert.Equal(3, PieChartHitTester.FindSliceIndex(values, SquareChart, new Point(20, 100)));
        Assert.Equal(1, PieChartHitTester.FindSliceIndex(values, SquareChart, new Point(100, 100)));
    }

    [Fact]
    public void FindSliceIndex_HandlesSingleSliceCenterAndOutsideCircle()
    {
        int[] values = [7];

        Assert.Equal(0, PieChartHitTester.FindSliceIndex(values, SquareChart, new Point(100, 100)));
        Assert.Equal(0, PieChartHitTester.FindSliceIndex(values, SquareChart, new Point(100, 10)));
        Assert.Null(PieChartHitTester.FindSliceIndex(values, SquareChart, new Point(5, 5)));
        Assert.Null(PieChartHitTester.FindSliceIndex([0, -1], SquareChart, new Point(100, 100)));
    }

    [Fact]
    public void FindSliceIndex_UsesMinimumDimensionAndRejectsInvalidGeometry()
    {
        int[] values = [1, 1];
        var wideChart = new Size(240, 100);

        Assert.Equal(0, PieChartHitTester.FindSliceIndex(values, wideChart, new Point(120, 10)));
        Assert.Null(PieChartHitTester.FindSliceIndex(values, wideChart, new Point(20, 50)));
        Assert.Null(PieChartHitTester.FindSliceIndex(values, new Size(0, 100), new Point(0, 0)));
        Assert.Null(PieChartHitTester.FindSliceIndex(values, Size.Empty, new Point(0, 0)));
        Assert.Null(PieChartHitTester.FindSliceIndex(values, SquareChart, new Point(double.NaN, 0)));
        Assert.Null(PieChartHitTester.FindSliceIndex(values, SquareChart, new Point(100, 100), 100));
    }
}
