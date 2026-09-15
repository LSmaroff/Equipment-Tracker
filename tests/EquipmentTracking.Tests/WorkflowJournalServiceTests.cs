using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;

namespace EquipmentTracking.Tests;

public sealed class WorkflowJournalServiceTests
{
    [Fact]
    public async Task PendingWorkflowCanBeSavedReloadedAndCompleted()
    {
        var folder = Path.Combine(Path.GetTempPath(), "EquipmentTrackingTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(folder);
            var logger = new FileLogger(paths);
            var journals = new WorkflowJournalService(paths, logger);
            var working = new WorkingTransaction
            {
                TransactionId = "TX-JOURNAL",
                CreatedAt = DateTimeOffset.Now,
                WorkingFolder = Path.Combine(folder, "Working", "TX-JOURNAL"),
                TemplateCopyPath = Path.Combine(folder, "template.pdf"),
                PreparedPdfPath = Path.Combine(folder, "prepared.pdf")
            };
            var journal = journals.CreateIntake(
                working,
                new CustomerIdentity { FirstName = "Test", LastName = "User" },
                "555-0100",
                "SSgt Tester",
                "58 SOW",
                "INC-1",
                [new DeviceRecord { Model = "M1", SerialNumber = "S1" }]);

            await journals.SaveAsync(journal);
            var pending = await journals.GetPendingAsync();
            var saved = Assert.Single(pending);
            Assert.Equal("PreparedPdf", saved.Stage);
            Assert.Equal("M1", Assert.Single(saved.Intake!.Devices).PartNumber);

            await journals.UpdateStageAsync(saved, "DatabaseUpdated", destinationPath: "C:\\final.pdf");
            var reloaded = await journals.GetAsync(saved.OperationId);
            Assert.NotNull(reloaded);
            Assert.Equal("DatabaseUpdated", reloaded.Stage);
            Assert.Equal("C:\\final.pdf", reloaded.DestinationPdfPath);

            await journals.MarkCompletedAsync(reloaded);
            Assert.Empty(await journals.GetPendingAsync());
        }
        finally
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
