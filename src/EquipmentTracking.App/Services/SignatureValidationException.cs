using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public sealed class SignatureValidationException : InvalidOperationException
{
    public SignatureValidationException(string message, SignatureDiagnosticReport diagnostics)
        : base(message)
    {
        Diagnostics = diagnostics;
    }

    public SignatureDiagnosticReport Diagnostics { get; }
}
