using System.Windows;
using System.Windows.Input;

namespace EquipmentTracking.App.Views;

public partial class ModelNameDialog : Window
{
    public ModelNameDialog(string partNumber)
    {
        InitializeComponent();
        PartNumberRun.Text = partNumber?.Trim() ?? string.Empty;
        Loaded += (_, _) =>
        {
            ModelNameBox.Focus();
            Keyboard.Focus(ModelNameBox);
        };
    }

    public string ModelName => ModelNameBox.Text.Trim();

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ModelName))
        {
            MessageBox.Show(
                this,
                "Enter a common model name or select Skip for now.",
                "Common model name",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            ModelNameBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void Skip_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ModelNameBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Save_OnClick(sender, e);
        }
    }
}
