using System.Collections.ObjectModel;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;

namespace EquipmentTracking.App.ViewModels;

public sealed class CloseoutDialogViewModel : ObservableObject
{
    private readonly AdobeService _adobe;
    private string _technician;
    private bool _confirmedSavedAndClosed;

    public CloseoutDialogViewModel(
        EquipmentTransaction transaction,
        IEnumerable<string> technicianSuggestions,
        AdobeService adobe,
        bool isPartialPickup = false)
    {
        Transaction = transaction;
        IsPartialPickup = isPartialPickup;
        _adobe = adobe;
        _technician = transaction.Technician;
        foreach (var name in technicianSuggestions
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            TechnicianSuggestions.Add(name);
        }

        OpenPdfCommand = new RelayCommand(() => _adobe.OpenPdf(Transaction.PdfPath));
    }

    public EquipmentTransaction Transaction { get; }
    public bool IsPartialPickup { get; }
    public bool CanEditTechnician => !IsPartialPickup;
    public string Title => IsPartialPickup ? "Sign pickup copy" : "Close out 1297";
    public string Subtitle => IsPartialPickup
        ? "Have the customer sign this pickup copy, then verify and save it under the original 1297."
        : "Complete the Pickup Signature in Adobe, then verify and archive this hand receipt.";
    public string TechnicianLabel => IsPartialPickup ? "Technician completing pickup" : "Technician completing closeout";
    public string FinalizeLabel => IsPartialPickup ? "Verify and save pickup" : "Finalize and archive";
    public ObservableCollection<string> TechnicianSuggestions { get; } = [];
    public RelayCommand OpenPdfCommand { get; }

    public string Technician
    {
        get => _technician;
        set
        {
            if (SetProperty(ref _technician, value ?? string.Empty))
            {
                NotifyFinalizeStateChanged();
            }
        }
    }

    public bool ConfirmedSavedAndClosed
    {
        get => _confirmedSavedAndClosed;
        set
        {
            if (SetProperty(ref _confirmedSavedAndClosed, value))
            {
                NotifyFinalizeStateChanged();
            }
        }
    }

    public bool CanFinalize => ConfirmedSavedAndClosed && !string.IsNullOrWhiteSpace(Technician);

    public string FinalizeRequirementMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Technician))
            {
                return "Enter or select the technician completing the closeout.";
            }

            if (!ConfirmedSavedAndClosed)
            {
                return "Confirm that Pickup Signature was applied, the PDF was saved, and Adobe is closed.";
            }

            return IsPartialPickup
                ? "Ready to verify this pickup signature and save only the selected devices as returned."
                : "Ready to verify the pickup signature and archive this 1297.";
        }
    }

    private void NotifyFinalizeStateChanged()
    {
        OnPropertyChanged(nameof(CanFinalize));
        OnPropertyChanged(nameof(FinalizeRequirementMessage));
    }
}
