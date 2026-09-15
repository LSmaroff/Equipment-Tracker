using System.Windows;
using System.Windows.Input;
using EquipmentTracking.App.ViewModels;

namespace EquipmentTracking.App.Views;

public partial class DeviceStatusDialog : Window
{
    public DeviceStatusDialog()
    {
        InitializeComponent();
        var workArea = SystemParameters.WorkArea;
        MaxWidth = Math.Max(MinWidth, workArea.Width - 32);
        MaxHeight = Math.Max(MinHeight, workArea.Height - 32);
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
        Loaded += (_, _) => PickupScanBox.Focus();
    }

    private void PickupScanBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        SelectScan();
    }

    private void SelectScan_OnClick(object sender, RoutedEventArgs e) => SelectScan();

    private void SelectScan()
    {
        if (DataContext is DeviceStatusDialogViewModel viewModel)
            viewModel.SelectScannedDevice();
        PickupScanBox.Focus();
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DeviceStatusDialogViewModel viewModel)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(viewModel.Technician))
        {
            MessageBox.Show(
                this,
                "Enter the technician name and grade.",
                "Update devices in 1297",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (viewModel.BuildUpdates().Count == 0)
        {
            MessageBox.Show(
                this,
                viewModel.IsPickupOnly ? "Select at least one device for pickup." : "No device status changes were selected.",
                "Update devices in 1297",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
