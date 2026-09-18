using EquipmentTracking.App.Models;
using EquipmentTracking.App.ViewModels;

namespace EquipmentTracking.Tests;

public sealed class TransactionDocumentsDialogViewModelTests
{
    [Fact]
    public void Documents_KeepPickupsUnderOneParent_WithoutDuplicatingFinalReceipt()
    {
        var transaction = new EquipmentTransaction
        {
            Id = "SYNTHETIC-PARENT", IsArchived = true, PdfPath = "final-pickup.pdf"
        };
        var viewModel = new TransactionDocumentsDialogViewModel(transaction,
        [
            Artifact("OriginalSignedIntake", "original.pdf"),
            Artifact("SignedPartialPickup", "first-pickup.pdf"),
            Artifact("SignedPartialPickup", "final-pickup.pdf")
        ],
        [
            new PickupReceipt { SequenceNumber = 2, PdfPath = "final-pickup.pdf", DeviceIds = [33], SignerName = "Synthetic Customer Two" },
            new PickupReceipt { SequenceNumber = 1, PdfPath = "first-pickup.pdf", DeviceIds = [11, 22], SignerName = "Synthetic Customer One" }
        ],
        [
            new TransactionDeviceStatusItem { DeviceId = 11, DeviceNumber = 1, SerialNumber = "SYN-ONE" },
            new TransactionDeviceStatusItem { DeviceId = 22, DeviceNumber = 2, SerialNumber = "SYN-TWO" },
            new TransactionDeviceStatusItem { DeviceId = 33, DeviceNumber = 3, SerialNumber = "SYN-THREE" }
        ], null!);
        Assert.Same(transaction, viewModel.Transaction);
        Assert.Equal(4, viewModel.Documents.Count);
        Assert.Equal("Signed pickup 1", viewModel.Documents[0].Name);
        Assert.Equal("Devices 1 (SYN-ONE), 2 (SYN-TWO)", viewModel.Documents[0].Details);
        Assert.Equal("Synthetic Customer Two", viewModel.Documents[1].Signer);
        Assert.Equal("Original signed intake", viewModel.Documents[2].Name);
        Assert.Single(viewModel.Documents, item => item.Path == "final-pickup.pdf" && !item.IsCurrentStatus);
        Assert.True(viewModel.Documents[3].IsCurrentStatus);
        Assert.Same(viewModel.Documents[3], viewModel.SelectedDocument);
    }

    [Fact]
    public void LegacyRecordWithoutArtifacts_StillShowsCurrentPdf()
    {
        var viewModel = new TransactionDocumentsDialogViewModel(
            new EquipmentTransaction { PdfPath = "legacy-working.pdf" }, [], [], [], null!);
        Assert.Equal("Current working 1297", Assert.Single(viewModel.Documents).Name);
        Assert.True(viewModel.OpenSelectedCommand.CanExecute(null));
        viewModel.SelectedDocument = null;
        Assert.False(viewModel.OpenSelectedCommand.CanExecute(null));
    }

    [Fact]
    public void PickupSignatureConfirmation_DoesNotOfferAnExtraCloseoutOrTechnicianEdit()
    {
        var viewModel = new CloseoutDialogViewModel(
            new EquipmentTransaction { Technician = "TSgt Synthetic Technician" }, [], null!, isPartialPickup: true);
        Assert.False(viewModel.CanEditTechnician);
        Assert.False(viewModel.CanFinalize);
        Assert.Equal("Verify and save pickup", viewModel.FinalizeLabel);
        viewModel.ConfirmedSavedAndClosed = true;
        Assert.True(viewModel.CanFinalize);
        Assert.Contains("only the selected devices", viewModel.FinalizeRequirementMessage);
    }

    private static FileArtifactRecord Artifact(string type, string path) => new()
    {
        TransactionId = "SYNTHETIC-PARENT", ArtifactType = type, Path = path
    };
}
