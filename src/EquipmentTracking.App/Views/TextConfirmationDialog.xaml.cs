using System.Windows;
using System.Windows.Controls;

namespace EquipmentTracking.App.Views;

public partial class TextConfirmationDialog : Window
{
    private readonly string _requiredText;

    public TextConfirmationDialog(
        string heading,
        string warning,
        string requiredText)
    {
        _requiredText = requiredText;
        InitializeComponent();
        HeadingText.Text = heading;
        WarningText.Text = warning;
        InstructionText.Text = $"Type {_requiredText} exactly to continue:";
        Loaded += (_, _) => ConfirmationText.Focus();
    }

    private void ConfirmationText_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ConfirmButton.IsEnabled = string.Equals(
            ConfirmationText.Text.Trim(),
            _requiredText,
            StringComparison.Ordinal);
    }

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmButton.IsEnabled)
        {
            return;
        }

        DialogResult = true;
    }
}
