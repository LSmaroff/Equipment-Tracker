using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using EquipmentTracking.App.Services;
using EquipmentTracking.App.ViewModels;

namespace EquipmentTracking.App.Views;

public partial class DashboardView : UserControl
{
    private readonly RecordCodeService _recordCodes = new();
    private Window? _hostWindow;
    private DashboardViewModel? _viewModel;
    private bool _pendingExactRecordSearch;
    private string? _pendingRecordId;
    private bool _searchCommandInFlight;
    private bool _selectAllWhenSearchCompletes;
    private string? _submittedRecordId;

    public DashboardView()
    {
        InitializeComponent();
        DataContextChanged += DashboardView_OnDataContextChanged;
        Loaded += DashboardView_OnLoaded;
        Unloaded += DashboardView_OnUnloaded;
    }

    private void DashboardView_OnDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded)
        {
            AttachViewModel(e.NewValue as DashboardViewModel);
        }
    }

    private void DashboardView_OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as DashboardViewModel);

        var hostWindow = Window.GetWindow(this);
        if (ReferenceEquals(_hostWindow, hostWindow))
        {
            return;
        }

        DetachHostWindow();
        _hostWindow = hostWindow;
        if (_hostWindow is not null)
        {
            _hostWindow.PreviewTextInput += HostWindow_OnPreviewTextInput;
        }
    }

    private void DashboardView_OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachHostWindow();
        DetachViewModel();
        _pendingExactRecordSearch = false;
        _pendingRecordId = null;
        _searchCommandInFlight = false;
        _selectAllWhenSearchCompletes = false;
    }

    private void AttachViewModel(DashboardViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        DetachViewModel();
        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.SearchCommand.CanExecuteChanged += SearchCommand_OnCanExecuteChanged;
        }
    }

    private void DetachViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.SearchCommand.CanExecuteChanged -= SearchCommand_OnCanExecuteChanged;
            _viewModel = null;
        }
    }

    private void DetachHostWindow()
    {
        if (_hostWindow is not null)
        {
            _hostWindow.PreviewTextInput -= HostWindow_OnPreviewTextInput;
            _hostWindow = null;
        }
    }

    private void HostWindow_OnPreviewTextInput(
        object sender,
        TextCompositionEventArgs e)
    {
        var focusedControlOwnsTextInput =
            TransactionSearchBox.IsKeyboardFocusWithin ||
            Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox or ComboBoxItem;
        var commandModifierPressed =
            (Keyboard.Modifiers &
             (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) !=
            ModifierKeys.None;
        if (!DashboardSearchInputRouter.TryCreateInitialQuery(
                e.Text,
                commandModifierPressed,
                focusedControlOwnsTextInput,
                out var initialQuery))
        {
            return;
        }

        if (!TransactionSearchBox.Focus())
        {
            return;
        }

        Keyboard.Focus(TransactionSearchBox);
        if (!TransactionSearchBox.IsKeyboardFocusWithin)
        {
            return;
        }

        // An off-box keystroke starts a fresh query. This prevents an old manual
        // query or prior QR code from being prefixed to the next scanner value.
        // SetCurrentValue preserves the XAML BindingExpression. Assigning Text
        // directly would replace the binding and leave SearchCommand reading a
        // stale view-model query after the focus transfer.
        TransactionSearchBox.SetCurrentValue(TextBox.TextProperty, initialQuery);
        TransactionSearchBox.CaretIndex = initialQuery.Length;
        e.Handled = true;
    }

    private void TransactionSearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        if (!_recordCodes.TryParse(textBox.Text, out var recordId))
        {
            // If a valid scan was waiting for a busy refresh/search, editing or
            // replacing that code cancels the deferred lookup. Never execute a
            // stale scanner request after the visible query has changed.
            _pendingExactRecordSearch = false;
            _pendingRecordId = null;
            return;
        }

        // TextChanged can run before the binding listener. Commit the full scanner
        // payload before SearchCommand reads SearchQuery.
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (_searchCommandInFlight &&
            string.Equals(recordId, _submittedRecordId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _pendingExactRecordSearch = true;
        _pendingRecordId = recordId;
        TryExecutePendingExactRecordSearch();
    }

    private void TransactionSearchBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (sender is TextBox textBox)
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        ExecuteSearch(selectAllWhenComplete: true);
    }

    private void TryExecutePendingExactRecordSearch()
    {
        if (!_pendingExactRecordSearch ||
            _viewModel is null ||
            !_viewModel.SearchCommand.CanExecute(null))
        {
            return;
        }

        _pendingExactRecordSearch = false;
        _submittedRecordId = _pendingRecordId;
        _pendingRecordId = null;
        ExecuteSearch(selectAllWhenComplete: true);
    }

    private bool ExecuteSearch(bool selectAllWhenComplete)
    {
        if (_viewModel is null || !_viewModel.SearchCommand.CanExecute(null))
        {
            return false;
        }

        _searchCommandInFlight = true;
        _selectAllWhenSearchCompletes = selectAllWhenComplete;
        _viewModel.SearchCommand.Execute(null);
        return true;
    }

    private void SearchCommand_OnCanExecuteChanged(object? sender, EventArgs e)
    {
        if (_viewModel is null || !_viewModel.SearchCommand.CanExecute(null))
        {
            return;
        }

        _searchCommandInFlight = false;
        if (_pendingExactRecordSearch)
        {
            TryExecutePendingExactRecordSearch();
            return;
        }

        if (!_selectAllWhenSearchCompletes)
        {
            return;
        }

        _selectAllWhenSearchCompletes = false;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (TransactionSearchBox.IsKeyboardFocusWithin)
                {
                    TransactionSearchBox.SelectAll();
                }
            }));
    }
}
