using EquipmentTracking.App.Models;
using EquipmentTracking.App.ViewModels;

namespace EquipmentTracking.Tests;

public sealed class CloseoutDialogViewModelTests
{
    [Fact]
    public void FinalizeState_RequiresTechnicianAndConfirmation()
    {
        var transaction = new EquipmentTransaction
        {
            TicketNumber = "TICKET-1",
            Technician = string.Empty
        };
        var viewModel = new CloseoutDialogViewModel(transaction, [], null!);

        Assert.False(viewModel.CanFinalize);
        Assert.Contains("technician", viewModel.FinalizeRequirementMessage, StringComparison.OrdinalIgnoreCase);

        viewModel.Technician = "A1C Example, Technician";

        Assert.False(viewModel.CanFinalize);
        Assert.Contains("Confirm", viewModel.FinalizeRequirementMessage, StringComparison.OrdinalIgnoreCase);

        viewModel.ConfirmedSavedAndClosed = true;

        Assert.True(viewModel.CanFinalize);
        Assert.Contains("Ready", viewModel.FinalizeRequirementMessage, StringComparison.OrdinalIgnoreCase);
    }
}
