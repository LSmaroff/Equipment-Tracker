using System.Windows;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Views;

public partial class PreflightDialog : Window
{
    private readonly PreflightReport _report;

    public PreflightDialog(PreflightReport report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        InitializeComponent();
        DataContext = report;
        SummaryText.Text =
            $"{report.PassedCount} ready, {report.WarningCount} warning(s), " +
            $"{report.FailedCount} failed. Completed {report.CompletedAt.LocalDateTime:g}.";
        ContinueButton.IsEnabled = !report.HasBlockingFailures;
        BlockingText.Text = report.HasBlockingFailures
            ? "A blocking readiness check failed. Correct it before using the application."
            : "Warnings do not prevent startup, but should be reviewed before operational use.";
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (_report.HasBlockingFailures)
        {
            return;
        }

        DialogResult = true;
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
