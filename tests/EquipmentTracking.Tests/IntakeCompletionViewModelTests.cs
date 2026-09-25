using EquipmentTracking.App.ViewModels;

namespace EquipmentTracking.Tests;

public sealed class IntakeCompletionViewModelTests
{
    [Fact]
    public void PrintIsUnavailableUntilSuccessfulCompletionAndClearsForNextIntake()
    {
        var printed = new List<string>();
        var model = new IntakeCompletionViewModel(() => false, id =>
        {
            printed.Add(id);
            return Task.CompletedTask;
        });
        Assert.False(model.HasCompletedIntake);
        Assert.False(model.PrintCommand.CanExecute(null));
        model.PrintCommand.Execute(null);
        Assert.Empty(printed);
        model.RecordCompletion("SYNTHETIC-SAVED-ID", "TICKET-ONE");
        Assert.True(model.HasCompletedIntake);
        Assert.Contains("TICKET-ONE", model.Summary);
        Assert.True(model.PrintCommand.CanExecute(null));
        model.PrintCommand.Execute(null);
        Assert.Equal("SYNTHETIC-SAVED-ID", Assert.Single(printed));
        model.Clear();
        Assert.False(model.HasCompletedIntake);
        Assert.False(model.PrintCommand.CanExecute(null));
        model.PrintCommand.Execute(null);
        Assert.Single(printed);
    }

    [Fact]
    public void BusyIntakeCannotPrintAndNewCompletionReplacesOldRecord()
    {
        var busy = true;
        string? printed = null;
        var model = new IntakeCompletionViewModel(() => busy, id =>
        {
            printed = id;
            return Task.CompletedTask;
        });
        model.RecordCompletion("OLD-ID", "OLD-TICKET");
        model.PrintCommand.Execute(null);
        Assert.Null(printed);
        model.Clear();
        model.RecordCompletion("NEW-ID", "NEW-TICKET");
        busy = false;
        model.PrintCommand.Execute(null);
        Assert.Equal("NEW-ID", printed);
        Assert.DoesNotContain("OLD-TICKET", model.Summary);
    }

    [Fact]
    public void CompletedRecordCanBePrintedAgainWithoutFinalizingAgain()
    {
        var attempts = 0;
        var model = new IntakeCompletionViewModel(() => false, _ =>
        {
            attempts++;
            // The presenter handles viewer/print errors; completion state must remain available for retry.
            return Task.CompletedTask;
        });
        model.RecordCompletion("SAVED-ID", "SAVED-TICKET");
        model.PrintCommand.Execute(null);
        model.PrintCommand.Execute(null);
        Assert.Equal(2, attempts);
        Assert.True(model.HasCompletedIntake);
        Assert.True(model.PrintCommand.CanExecute(null));
    }

    [Fact]
    public async Task PrintCommandRejectsDoubleClickWhileSheetIsOpen()
    {
        var finished = new TaskCompletionSource();
        var attempts = 0;
        var model = new IntakeCompletionViewModel(() => false, _ =>
        {
            attempts++;
            return finished.Task;
        });
        model.RecordCompletion("SAVED-ID", "TICKET");
        model.PrintCommand.Execute(null);
        model.PrintCommand.Execute(null);
        Assert.Equal(1, attempts);
        Assert.False(model.PrintCommand.CanExecute(null));
        finished.SetResult();
        for (var retry = 0; retry < 50 && !model.PrintCommand.CanExecute(null); retry++) await Task.Delay(10);
        Assert.True(model.PrintCommand.CanExecute(null));
        Assert.True(model.HasCompletedIntake);
    }

    [Fact]
    public void EmptyCompletionCannotEnablePrinting()
    {
        var model = new IntakeCompletionViewModel(() => false, _ => Task.CompletedTask);
        Assert.Throws<ArgumentException>(() => model.RecordCompletion("", "TICKET"));
        Assert.False(model.HasCompletedIntake);
        Assert.False(model.PrintCommand.CanExecute(null));
    }
}
