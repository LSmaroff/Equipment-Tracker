using System.Globalization;

namespace EquipmentTracking.App.Models;

public sealed class SignatureDiagnosticReport
{
    public string PdfPath { get; init; } = string.Empty;
    public string ExpectedFieldName { get; init; } = string.Empty;
    public bool PdfExists { get; init; }
    public bool PdfChangedSinceCloseoutStarted { get; init; }
    public bool ExpectedFieldNameFoundInRawObjects { get; init; }
    public int FieldSpecificCandidateCount { get; init; }
    public int TotalCandidateCount { get; init; }
    public int NewCandidateCount { get; init; }
    public bool ReadableFieldSpecificSignatureFound { get; init; }
    public string FieldSpecificSignerName { get; init; } = string.Empty;
    public DateTimeOffset? FieldSpecificSigningTime { get; init; }
    public bool ReadableNewSignatureFound { get; init; }
    public string NewSignatureSignerName { get; init; } = string.Empty;
    public DateTimeOffset? NewSignatureSigningTime { get; init; }
    public string Details { get; init; } = string.Empty;

    public string ToDisplayText()
    {
        return string.Join(Environment.NewLine,
        [
            $"PDF: {PdfPath}",
            $"Expected field: {ExpectedFieldName}",
            $"PDF exists: {YesNo(PdfExists)}",
            $"PDF changed since closeout began: {YesNo(PdfChangedSinceCloseoutStarted)}",
            $"Field name visible in raw PDF objects: {YesNo(ExpectedFieldNameFoundInRawObjects)}",
            $"Field-specific signature containers found: {FieldSpecificCandidateCount}",
            $"All signature containers found: {TotalCandidateCount}",
            $"New signature containers found: {NewCandidateCount}",
            $"Readable field-specific signature: {YesNo(ReadableFieldSpecificSignatureFound)}",
            $"Field-specific signer: {DisplayValue(FieldSpecificSignerName)}",
            $"Field-specific signing time: {DisplayDate(FieldSpecificSigningTime)}",
            $"Readable newly-added signature: {YesNo(ReadableNewSignatureFound)}",
            $"New signature signer: {DisplayValue(NewSignatureSignerName)}",
            $"New signature signing time: {DisplayDate(NewSignatureSigningTime)}",
            string.Empty,
            Details
        ]);
    }

    private static string YesNo(bool value) => value ? "Yes" : "No";
    private static string DisplayValue(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Not available" : value;
    private static string DisplayDate(DateTimeOffset? value) =>
        value is null ? "Not available" : value.Value.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
}
