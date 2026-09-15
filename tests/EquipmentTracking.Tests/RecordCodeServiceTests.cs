using EquipmentTracking.App.Services;

namespace EquipmentTracking.Tests;

public sealed class RecordCodeServiceTests
{
    private const string TransactionId = "TX-20260810-143025-1A2B3C4D";

    [Fact]
    public void PayloadRoundTripsToTransactionId()
    {
        var service = new RecordCodeService();

        var payload = service.BuildPayload(TransactionId);

        Assert.Equal("ETP1297:" + TransactionId, payload);
        Assert.True(service.TryParse(payload, out var parsed));
        Assert.Equal(TransactionId, parsed);
        Assert.True(service.LooksLikeRecordCode(TransactionId.ToLowerInvariant()));
    }

    [Fact]
    public void IncompleteScannerPayloadPrefixesDoNotParseAsRecordCodes()
    {
        var service = new RecordCodeService();
        var payload = service.BuildPayload(TransactionId);

        for (var length = 1; length < payload.Length; length++)
        {
            var incompletePayload = payload[..length];

            Assert.False(
                service.TryParse(incompletePayload, out _),
                $"The incomplete scanner payload '{incompletePayload}' must not trigger a record search.");
        }

        Assert.True(service.TryParse(payload, out var parsed));
        Assert.Equal(TransactionId, parsed);
    }

    [Fact]
    public void GeneratedCodeIsPng()
    {
        var png = new RecordCodeService().GeneratePng(TransactionId);

        Assert.True(png.Length > 100);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ETP1297:TX-BAD")]
    [InlineData("TX-20260810-143025-NOTHEX00")]
    public void RejectsInvalidCodes(string value)
    {
        Assert.False(new RecordCodeService().TryParse(value, out _));
    }
}
