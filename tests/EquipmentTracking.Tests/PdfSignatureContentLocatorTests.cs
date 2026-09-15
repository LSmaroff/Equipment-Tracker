using EquipmentTracking.App.Services;

namespace EquipmentTracking.Tests;

public sealed class PdfSignatureContentLocatorTests
{
    [Fact]
    public void PreferredFieldSignature_IsReturnedBeforeOtherSignatures()
    {
        const string pdfText =
            "1 0 obj << /FT /Sig /T (ISSUED TO SIGNATURE) /V 3 0 R >> endobj\n" +
            "2 0 obj << /FT /Sig /T (ISSUED BY SIGNATURE) /V 4 0 R >> endobj\n" +
            "3 0 obj << /Type /Sig /Contents <AABB> >> endobj\n" +
            "4 0 obj << /Type /Sig /Contents <CCDD> >> endobj";

        var candidates = PdfSignatureContentLocator.FindCandidateHexContents(
            pdfText,
            "ISSUED BY SIGNATURE");

        Assert.Equal("CCDD", candidates[0]);
        Assert.Contains("AABB", candidates);
    }

    [Fact]
    public void EscapedLiteralFieldName_IsDecoded()
    {
        const string pdfText =
            "1 0 obj << /FT /Sig /T (ISSUED\\040BY\\040SIGNATURE) /V 2 0 R >> endobj\n" +
            "2 0 obj << /Type /Sig /Contents <CAFE> >> endobj";

        var candidates = PdfSignatureContentLocator.FindCandidateHexContents(
            pdfText,
            "ISSUED BY SIGNATURE");

        Assert.Equal("CAFE", candidates[0]);
    }

    [Fact]
    public void PreferredFieldOnly_DoesNotFallBack_ToAnotherSignature()
    {
        const string pdfText =
            "1 0 obj << /FT /Sig /T (ISSUED BY SIGNATURE) /V 2 0 R >> endobj\n" +
            "2 0 obj << /Type /Sig /Contents <CAFE> >> endobj";

        var candidates = PdfSignatureContentLocator.FindCandidateHexContents(
            pdfText,
            "Pickup Signature",
            preferredFieldOnly: true);

        Assert.Empty(candidates);
    }

    [Fact]
    public void PreferredFieldSignature_IsFoundThroughKidWidgetValue()
    {
        const string pdfText =
            "1 0 obj << /FT /Sig /T (Pickup Signature) /Kids [2 0 R] >> endobj\n" +
            "2 0 obj << /Parent 1 0 R /Subtype /Widget /V 3 0 R >> endobj\n" +
            "3 0 obj << /Type /Sig /Contents <BEEF> >> endobj";

        var candidates = PdfSignatureContentLocator.FindCandidateHexContents(
            pdfText,
            "Pickup Signature",
            preferredFieldOnly: true);

        Assert.Equal("BEEF", candidates[0]);
    }

    [Fact]
    public void PreferredFieldSignature_MatchesWhitespaceNormalizedName()
    {
        const string pdfText =
            "1 0 obj << /FT /Sig /T (Pickup    Signature ) /V 2 0 R >> endobj\n" +
            "2 0 obj << /Type /Sig /Contents <F00D> >> endobj";

        var candidates = PdfSignatureContentLocator.FindCandidateHexContents(
            pdfText,
            "Pickup Signature",
            preferredFieldOnly: true);

        Assert.Equal("F00D", candidates[0]);
    }

}
