using System.Text;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Services;

namespace EquipmentTracking.Tests;

public sealed class SignatureExtractionServiceTests
{
    [Fact]
    public async Task SignatureBaseline_ChangesWhenAnotherSignatureContainerIsAdded()
    {
        var path = Path.Combine(Path.GetTempPath(), $"signature-baseline-{Guid.NewGuid():N}.pdf");
        try
        {
            var service = new SignatureExtractionService(
                new CertificateNameParser(),
                new FileLogger(new AppPaths()));

            await File.WriteAllTextAsync(
                path,
                "%PDF-1.7\n1 0 obj << /Type /Sig /Contents <AABB> >> endobj\n%%EOF",
                Encoding.Latin1);
            var before = await service.GetSignatureFingerprintsAsync(path);

            await File.WriteAllTextAsync(
                path,
                "%PDF-1.7\n" +
                "1 0 obj << /Type /Sig /Contents <AABB> >> endobj\n" +
                "2 0 obj << /Type /Sig /Contents <CCDD> >> endobj\n%%EOF",
                Encoding.Latin1);
            var after = await service.GetSignatureFingerprintsAsync(path);

            Assert.Single(before);
            Assert.Equal(2, after.Count);
            Assert.All(before, fingerprint => Assert.Contains(fingerprint, after));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
