using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Controls;

public sealed class PieChart : FrameworkElement
{
    private const double HoverOutlineThickness = 5d;
    private PieChartSlice? _hoveredSlice;
    private ToolTip? _hoverToolTip;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(PieChart),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                ItemsSourceChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public PieChart()
    {
        Unloaded += PieChart_OnUnloaded;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var slices = GetSlices()
            .Where(slice => slice.Value > 0)
            .ToArray();

        var total = slices.Sum(slice => (double)slice.Value);
        var radius = Math.Max(
            0,
            Math.Min(ActualWidth, ActualHeight) / 2d - PieChartHitTester.DefaultPadding);
        var center = new Point(ActualWidth / 2d, ActualHeight / 2d);
        if (total <= 0 || radius <= 0)
        {
            DrawEmptyState(drawingContext, center, radius);
            return;
        }

        var outline = new Pen(
            Application.Current.TryFindResource("SurfaceBrush") as Brush ?? Brushes.White,
            2);

        if (slices.Length == 1)
        {
            drawingContext.DrawEllipse(slices[0].Brush, outline, center, radius, radius);
            if (ReferenceEquals(_hoveredSlice, slices[0]))
            {
                drawingContext.DrawEllipse(
                    brush: null,
                    CreateHoverOutline(),
                    center,
                    radius,
                    radius);
            }

            return;
        }

        Geometry? hoveredGeometry = null;
        var currentAngle = -90d;
        foreach (var slice in slices)
        {
            var sweep = 360d * slice.Value / total;
            var start = PointOnCircle(center, radius, currentAngle);
            var end = PointOnCircle(center, radius, currentAngle + sweep);

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(center, isFilled: true, isClosed: true);
                context.LineTo(start, isStroked: true, isSmoothJoin: true);
                context.ArcTo(
                    end,
                    new Size(radius, radius),
                    rotationAngle: 0,
                    isLargeArc: sweep > 180,
                    sweepDirection: SweepDirection.Clockwise,
                    isStroked: true,
                    isSmoothJoin: true);
            }

            geometry.Freeze();
            drawingContext.DrawGeometry(slice.Brush, outline, geometry);
            if (ReferenceEquals(_hoveredSlice, slice))
            {
                hoveredGeometry = geometry;
            }

            currentAngle += sweep;
        }

        // Draw the emphasis last so neighboring wedges cannot cover the shared
        // edge of the hovered category.
        if (hoveredGeometry is not null)
        {
            drawingContext.DrawGeometry(
                brush: null,
                CreateHoverOutline(),
                hoveredGeometry);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var slices = GetSlices();
        var hoveredIndex = PieChartHitTester.FindSliceIndex(
            slices.Select(slice => slice.Value).ToArray(),
            RenderSize,
            e.GetPosition(this));
        SetHoveredSlice(hoveredIndex is int index ? slices[index] : null);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ClearHover();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ClearHover();
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new FrameworkElementAutomationPeer(this);

    private static void ItemsSourceChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var chart = (PieChart)dependencyObject;

        chart.ClearHover();

        if (args.OldValue is INotifyCollectionChanged oldCollection)
        {
            CollectionChangedEventManager.RemoveHandler(
                oldCollection,
                chart.ItemsCollectionChanged);
        }

        if (args.NewValue is INotifyCollectionChanged newCollection)
        {
            CollectionChangedEventManager.AddHandler(
                newCollection,
                chart.ItemsCollectionChanged);
        }

        chart.InvalidateVisual();
    }

    private void ItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ClearHover();
        InvalidateVisual();
    }

    private PieChartSlice[] GetSlices() =>
        ItemsSource?.Cast<object>()
            .OfType<PieChartSlice>()
            .ToArray() ?? [];

    private void SetHoveredSlice(PieChartSlice? slice)
    {
        if (ReferenceEquals(_hoveredSlice, slice))
        {
            return;
        }

        _hoveredSlice = slice;
        if (slice is null)
        {
            CloseHoverToolTip();
        }
        else
        {
            _hoverToolTip ??= new ToolTip
            {
                Placement = PlacementMode.MousePoint,
                PlacementTarget = this,
                HorizontalOffset = 12,
                VerticalOffset = 12,
                Focusable = false,
                IsHitTestVisible = false,
                StaysOpen = true
            };
            _hoverToolTip.IsOpen = false;
            _hoverToolTip.Content = slice.DisplayText;
            _hoverToolTip.IsOpen = true;
        }

        InvalidateVisual();
    }

    private void ClearHover()
    {
        if (_hoveredSlice is null && _hoverToolTip?.IsOpen != true)
        {
            return;
        }

        _hoveredSlice = null;
        CloseHoverToolTip();
        InvalidateVisual();
    }

    private void CloseHoverToolTip()
    {
        if (_hoverToolTip is null)
        {
            return;
        }

        _hoverToolTip.IsOpen = false;
        _hoverToolTip.Content = null;
    }

    private void PieChart_OnUnloaded(object sender, RoutedEventArgs e) => ClearHover();

    private static Pen CreateHoverOutline() => new(
        Application.Current.TryFindResource("FocusBrush") as Brush
            ?? Brushes.DodgerBlue,
        HoverOutlineThickness)
    {
        LineJoin = PenLineJoin.Round
    };

    private static void DrawEmptyState(
        DrawingContext drawingContext,
        Point center,
        double radius)
    {
        if (radius <= 0)
        {
            return;
        }

        var fill = Application.Current.TryFindResource("SurfaceTertiaryBrush") as Brush
            ?? Brushes.LightGray;
        var outline = new Pen(
            Application.Current.TryFindResource("BorderNeutralBrush") as Brush
                ?? Brushes.Gray,
            1);
        drawingContext.DrawEllipse(fill, outline, center, radius, radius);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180d;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }
}
