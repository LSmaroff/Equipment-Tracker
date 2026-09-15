using System.Windows;

namespace EquipmentTracking.App.Views;

public partial class TransactionDocumentsDialog : Window
{
    public TransactionDocumentsDialog()
    {
        InitializeComponent();
        var workArea = SystemParameters.WorkArea;
        MaxWidth = Math.Max(MinWidth, workArea.Width - 32);
        MaxHeight = Math.Max(MinHeight, workArea.Height - 32);
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
    }
}
