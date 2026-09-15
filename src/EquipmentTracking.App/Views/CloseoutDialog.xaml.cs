using System.Windows;
using EquipmentTracking.App.ViewModels;

namespace EquipmentTracking.App.Views;

public partial class CloseoutDialog : Window
{
    public CloseoutDialog()
    {
        InitializeComponent();

        var workArea = SystemParameters.WorkArea;
        MaxWidth = Math.Max(MinWidth, workArea.Width - 32);
        MaxHeight = Math.Max(MinHeight, workArea.Height - 32);
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
    }

    private void Finalize_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CloseoutDialogViewModel viewModel || !viewModel.CanFinalize)
        {
            return;
        }

        DialogResult = true;
    }
}
