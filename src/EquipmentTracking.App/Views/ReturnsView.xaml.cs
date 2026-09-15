using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EquipmentTracking.App.Services;
using EquipmentTracking.App.ViewModels;

namespace EquipmentTracking.App.Views;

public partial class ReturnsView : UserControl
{
    private readonly DispatcherTimer _recordScanTimer;
    private readonly RecordCodeService _recordCodes = new();

    public ReturnsView()
    {
        InitializeComponent();
        _recordScanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _recordScanTimer.Tick += RecordScanTimer_OnTick;
        Unloaded += (_, _) => _recordScanTimer.Stop();
    }

    private void EquipmentSearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _recordScanTimer.Stop();
        if (sender is TextBox textBox && _recordCodes.LooksLikeRecordCode(textBox.Text))
        {
            _recordScanTimer.Start();
        }
    }

    private void RecordScanTimer_OnTick(object? sender, EventArgs e)
    {
        _recordScanTimer.Stop();
        ExecuteSearch();
    }

    private void EquipmentSearchBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ReturnsViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        _recordScanTimer.Stop();
        if (viewModel.SearchCommand.CanExecute(null))
        {
            viewModel.SearchCommand.Execute(null);
        }
    }

    private void ExecuteSearch()
    {
        if (DataContext is ReturnsViewModel viewModel &&
            viewModel.SearchCommand.CanExecute(null))
        {
            viewModel.SearchCommand.Execute(null);
        }
    }

    private void EquipmentResultsGrid_OnMouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            DataContext is not ReturnsViewModel viewModel ||
            viewModel.SelectedResult is null)
        {
            return;
        }

        e.Handled = true;
        if (viewModel.OpenPdfCommand.CanExecute(null))
        {
            viewModel.OpenPdfCommand.Execute(null);
        }
    }
}
