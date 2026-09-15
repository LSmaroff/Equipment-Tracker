using System.Windows;

namespace EquipmentTracking.App.Views;

public partial class DiagnosticDetailsDialog : Window
{
    public DiagnosticDetailsDialog(string heading, string message, string details)
    {
        InitializeComponent();
        HeadingText.Text = heading;
        MessageText.Text = message;
        DetailsText.Text = details;
    }

    public static void Show(string heading, string message, string details)
    {
        var dialog = new DiagnosticDetailsDialog(heading, message, details)
        {
            Owner = Application.Current?.MainWindow
        };
        dialog.ShowDialog();
    }

    private void CopyDetails_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DetailsText.Text ?? string.Empty);
        }
        catch
        {
            MessageBox.Show(
                "The details could not be copied to the clipboard.",
                "Copy details",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
