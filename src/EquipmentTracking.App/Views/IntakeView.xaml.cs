using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.ViewModels;

namespace EquipmentTracking.App.Views;

public partial class IntakeView : UserControl
{
    private IntakeViewModel? _viewModel;
    private readonly DispatcherTimer _scanIdleTimer;
    private TextBox? _pendingScanBox;
    private DateTime _scanStartedUtc;
    private int _previousScanLength;
    private bool _autoSubmitting;

    public IntakeView()
    {
        InitializeComponent();
        _scanIdleTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(325),
            DispatcherPriority.Input,
            ScanIdleTimer_OnTick,
            Dispatcher);
        _scanIdleTimer.Stop();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        AttachViewModel(e.NewValue as IntakeViewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as IntakeViewModel);
    }

    private void AttachViewModel(IntakeViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        DetachViewModel();
        _viewModel = viewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.DeviceEntries.CollectionChanged += OnDeviceEntriesChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _scanIdleTimer.Stop();
        _pendingScanBox = null;
        DetachViewModel();
    }

    private void DetachViewModel()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.DeviceEntries.CollectionChanged -= OnDeviceEntriesChanged;
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IntakeViewModel.HasActiveTransaction) &&
            _viewModel?.CanEnterDevices == true)
        {
            QueueScanFocus();
        }
    }

    private void OnDeviceEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel?.CanEnterDevices == true)
        {
            QueueScanFocus();
        }
    }

    private async void DeviceEntry_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter ||
            sender is not TextBox textBox ||
            textBox.Tag is not DeviceEntryRow entry ||
            DataContext is not IntakeViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        _scanIdleTimer.Stop();
        _pendingScanBox = null;
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        var saved = ReferenceEquals(textBox, FindScanTextBoxForEntry(entry))
            ? await viewModel.ProcessScanAsync(entry)
            : await viewModel.CommitOrUpdateEntryAsync(entry);

        if (saved && !entry.IsCommitted)
        {
            return;
        }

        if (saved)
        {
            QueueScanFocus();
        }
    }

    private void ScanTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_autoSubmitting ||
            sender is not TextBox textBox ||
            textBox.Tag is not DeviceEntryRow { IsCommitted: false } ||
            string.IsNullOrWhiteSpace(textBox.Text))
        {
            return;
        }

        if (!ReferenceEquals(_pendingScanBox, textBox) ||
            textBox.Text.Length <= _previousScanLength)
        {
            _pendingScanBox = textBox;
            _scanStartedUtc = DateTime.UtcNow;
        }

        _previousScanLength = textBox.Text.Length;
        _scanIdleTimer.Stop();
        _scanIdleTimer.Start();
    }

    private async void ScanIdleTimer_OnTick(object? sender, EventArgs e)
    {
        _scanIdleTimer.Stop();
        var textBox = _pendingScanBox;
        _pendingScanBox = null;
        if (_autoSubmitting ||
            textBox?.Tag is not DeviceEntryRow { IsCommitted: false } entry ||
            DataContext is not IntakeViewModel viewModel)
        {
            return;
        }

        var scan = textBox.Text;
        var averageMillisecondsPerCharacter =
            (DateTime.UtcNow - _scanStartedUtc).TotalMilliseconds /
            Math.Max(1, scan.Length);
        if (scan.Length < 8 ||
            (!LooksScannerFormatted(scan) && averageMillisecondsPerCharacter > 80d))
        {
            return;
        }

        try
        {
            _autoSubmitting = true;
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (await viewModel.ProcessScanAsync(entry))
            {
                QueueScanFocus();
            }
        }
        finally
        {
            _autoSubmitting = false;
            _previousScanLength = 0;
        }
    }

    private static bool LooksScannerFormatted(string value)
    {
        return value.Contains("[)>", StringComparison.Ordinal) ||
               value.Contains("17V", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("18S", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("1P", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("\\0000", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("«GS»", StringComparison.OrdinalIgnoreCase);
    }

    private TextBox? FindScanTextBoxForEntry(DeviceEntryRow entry)
    {
        return FindVisualChildren<TextBox>(DeviceEntryList)
            .FirstOrDefault(box => ReferenceEquals(box.Tag, entry) &&
                                   Grid.GetColumn(box) == 2);
    }

    private void QueueScanFocus()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(FocusFirstPendingScanBox));
    }

    private void FocusFirstPendingScanBox()
    {
        if (_viewModel?.CanEnterDevices != true)
        {
            return;
        }

        DeviceEntryList.UpdateLayout();

        foreach (var textBox in FindVisualChildren<TextBox>(DeviceEntryList))
        {
            if (textBox.Tag is DeviceEntryRow { IsCommitted: false })
            {
                textBox.Focus();
                Keyboard.Focus(textBox);
                textBox.SelectAll();
                return;
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
