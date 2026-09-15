using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using EquipmentTracking.App.Controls;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.Tests;

public sealed class PieChartLifecycleTests
{
    [Fact]
    public void PieChart_ExposesAutomationNameAndHelpText()
    {
        Exception? testException = null;
        string? peerName = null;
        string? peerHelpText = null;
        var thread = new Thread(() =>
        {
            try
            {
                var chart = new PieChart();
                AutomationProperties.SetName(chart, "Active device distribution chart");
                AutomationProperties.SetHelpText(
                    chart,
                    "Hover a slice to identify its category, count, and percentage.");
                var peer = UIElementAutomationPeer.CreatePeerForElement(chart);
                peerName = peer?.GetName();
                peerHelpText = peer?.GetHelpText();
            }
            catch (Exception ex)
            {
                testException = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF automation test did not complete.");
        if (testException is not null)
        {
            ExceptionDispatchInfo.Capture(testException).Throw();
        }

        Assert.Equal("Active device distribution chart", peerName);
        Assert.Contains("category, count, and percentage", peerHelpText);
    }

    [Fact]
    public void PieChart_CollectionSubscriptionDoesNotRetainUnloadedChart()
    {
        Exception? testException = null;
        WeakReference? chartReference = null;
        ObservableCollection<PieChartSlice>? retainedSlices = null;
        var thread = new Thread(() =>
        {
            try
            {
                retainedSlices =
                [
                    new PieChartSlice
                    {
                        Label = "Available",
                        Value = 1,
                        Percentage = 100
                    }
                ];
                chartReference = CreateChartReference(retainedSlices);
            }
            catch (Exception ex)
            {
                testException = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF lifecycle test did not complete.");
        if (testException is not null)
        {
            ExceptionDispatchInfo.Capture(testException).Throw();
        }

        Assert.NotNull(chartReference);
        Assert.NotNull(retainedSlices);
        for (var attempt = 0; attempt < 3 && chartReference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(chartReference.IsAlive);
        retainedSlices.Add(new PieChartSlice { Label = "Issued", Value = 1 });
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateChartReference(
        ObservableCollection<PieChartSlice> slices)
    {
        var chart = new PieChart { ItemsSource = slices };
        return new WeakReference(chart);
    }
}
