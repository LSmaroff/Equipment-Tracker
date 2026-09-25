using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using EquipmentTracking.App.ViewModels;
using EquipmentTracking.App.Views;

namespace EquipmentTracking.Tests;

public sealed class PickupDialogLayoutTests
{
    [Fact]
    public void PickupAndDocumentsDialogs_LoadAndKeepActionsVisible()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
                using var stream = File.OpenRead(Path.Combine(root, "src", "EquipmentTracking.App", "Themes", "LayoutStyles.xaml"));
                // Resource-only Application: never run startup or initialize operational services/data.
                var application = new Application
                {
                    Resources = (ResourceDictionary)XamlReader.Load(stream),
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                var devices = new[]
                {
                    new TransactionDeviceStatusItem { DeviceId = 1, DeviceNumber = 1, Model = "HP EliteBook 645", PartNumber = "A4TH1AV", SerialNumber = "SYNTHETIC-ONE", OriginalStatus = DeviceStatusCatalog.ReadyForPickup, Status = DeviceStatusCatalog.ReadyForPickup, IsPickedUp = true },
                    new TransactionDeviceStatusItem { DeviceId = 2, DeviceNumber = 2, Model = "Synthetic Test Device", PartNumber = "SYN-PART-2", SerialNumber = "SYNTHETIC-TWO", OriginalStatus = DeviceStatusCatalog.InShop, Status = DeviceStatusCatalog.InShop }
                };
                var transaction = new EquipmentTransaction
                {
                    Id = "SYNTHETIC-LAYOUT", TicketNumber = "SYNTHETIC-1297", Technician = "TSgt Synthetic Technician",
                    Customer = new CustomerIdentity { Rank = "MSgt", FirstName = "Test", LastName = "Customer" },
                    Organization = "Synthetic Test Unit", PdfPath = "synthetic-original.pdf"
                };
                var pickup = new DeviceStatusDialog
                {
                    DataContext = new DeviceStatusDialogViewModel(new RecentTransactionItem
                    {
                        TransactionId = transaction.Id, CustomerName = transaction.Customer.DisplayName,
                        TicketNumber = transaction.TicketNumber, Technician = transaction.Technician
                    }, devices, [transaction.Technician], pickupOnly: true)
                };
                RenderAndCheck(pickup, 980, 680, "pickup-selection");
                var documents = new TransactionDocumentsDialog
                {
                    DataContext = new TransactionDocumentsDialogViewModel(transaction,
                        [new FileArtifactRecord { ArtifactType = "OriginalSignedIntake", Path = "synthetic-original.pdf" }],
                        [new PickupReceipt { SequenceNumber = 1, DeviceIds = [1], SignerName = "Synthetic Customer", PdfPath = "synthetic-pickup.pdf" }],
                        devices, null!)
                };
                RenderAndCheck(documents, 980, 550, "pickup-documents");
                RenderAndCheck(documents, 860, 470, "pickup-documents-minimum");
                // Render the actual completed-intake panel without constructing application services.
                var intakeXaml = System.Xml.Linq.XDocument.Load(Path.Combine(root, "src", "EquipmentTracking.App", "Views", "IntakeView.xaml"));
                var completionPanel = intakeXaml.Descendants().Single(element =>
                    (string?)element.Attribute("DataContext") == "{Binding Completion}");
                var panel = Assert.IsAssignableFrom<FrameworkElement>(XamlReader.Parse(completionPanel.ToString()));
                var completion = new IntakeCompletionViewModel(() => false, _ => Task.CompletedTask);
                completion.RecordCompletion("SYNTHETIC-SAVED", "SYNTHETIC-INTAKE-1297");
                var intakeContainer = new Grid();
                intakeContainer.Children.Add(panel);
                var intakePrint = new Window { Content = intakeContainer, DataContext = new { Completion = completion } };
                RenderAndCheck(intakePrint, 620, 180, "intake-completed-print");
                Assert.Contains(Descendants<Button>(panel), button => button.Content?.ToString() == "Print two 1297 copies" &&
                    button.IsEnabled && ReferenceEquals(button.Command, completion.PrintCommand));
                Assert.Contains(Descendants<TextBlock>(panel), text => text.Text.Contains("SYNTHETIC-INTAKE-1297", StringComparison.Ordinal));
                Assert.Equal(Visibility.Visible, panel.Visibility);
                completion.Clear();
                intakeContainer.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, panel.Visibility);
                intakePrint.Close();
                var signature = new CloseoutDialog
                {
                    DataContext = new CloseoutDialogViewModel(transaction, [], null!, isPartialPickup: true)
                };
                RenderAndCheck(signature, 660, 550, "pickup-signature");
                pickup.Close();
                documents.Close();
                signature.Close();
                application.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Pickup dialog layout did not complete.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void RenderAndCheck(Window window, int width, int height, string name)
    {
        var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        content.DataContext = window.DataContext;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { },
            System.Windows.Threading.DispatcherPriority.ContextIdle);
        content.UpdateLayout();
        foreach (var dataGrid in Descendants<DataGrid>(content))
            foreach (var column in dataGrid.Columns.Where(column => column.Width.IsStar))
                Assert.True(column.ActualWidth >= 140, $"{name}: data column collapsed below readable width.");
        var buttons = Descendants<Button>(content).ToArray();
        Assert.NotEmpty(buttons);
        foreach (var button in buttons.Where(button => button.Visibility == Visibility.Visible && button.ActualHeight > 0))
        {
            var bounds = button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));
            Assert.True(bounds.Left >= -1, $"{name}: button beyond left edge ({bounds.Left}).");
            Assert.True(bounds.Bottom <= height + 1, $"{name}: button below dialog ({bounds.Bottom}).");
            Assert.True(bounds.Right <= width + 1, $"{name}: button beyond dialog ({bounds.Right}).");
        }
        var output = Environment.GetEnvironmentVariable("ETP_UI_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(file);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
