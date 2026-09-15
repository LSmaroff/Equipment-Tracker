using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;

namespace EquipmentTracking.Tests;

public sealed class DeviceRecognitionServiceTests
{
    private readonly BarcodeParser _parser = new();

    [Theory]
    [InlineData(">«RS»06«GS»18S7ESQ72MQ5390WTS")]
    [InlineData("[)>\u001e06\u001d18S7ESQ72MQ5390WTS\u001e\u0004")]
    [InlineData("[)>½RSW06½GSW18S7ESQ72MQ5390WTS")]
    public async Task RecognizesExactHpIdentityAcrossScannerSeparatorVariants(string scan)
    {
        var parsed = _parser.Parse(scan);
        var service = CreateService(new FakeRecognitionDataSource());

        var result = await service.RecognizeAsync(parsed);

        Assert.True(parsed.Parsed);
        Assert.Equal(string.Empty, parsed.PartNumber);
        Assert.Equal("7ESQ7", parsed.CageCode);
        Assert.Equal("2MQ5390WTS", parsed.SerialNumber);
        Assert.Equal(scan, parsed.RawValue);
        Assert.Equal("A4TH1AV", result.PartNumber);
        Assert.Equal("HP EliteBook 645", result.ModelName);
        Assert.Equal("Exact built-in device identity", result.Source);
    }

    [Theory]
    [InlineData("[)>0618S7ESQ72MQ5390WTX")]
    [InlineData("[)>0618SABCDE2MQ5390WTS")]
    public async Task ExactKnownDeviceDoesNotGuessFromCageOrSerialAlone(string scan)
    {
        var result = await CreateService(new FakeRecognitionDataSource())
            .RecognizeAsync(_parser.Parse(scan));

        Assert.Equal(string.Empty, result.PartNumber);
        Assert.Equal(string.Empty, result.ModelName);
    }

    [Fact]
    public async Task BuiltInPartMappingSuppliesModelName()
    {
        var result = await CreateService(new FakeRecognitionDataSource())
            .RecognizeAsync(_parser.Parse("PN=A4TH1AV;SN=UNRELATED01"));

        Assert.Equal("A4TH1AV", result.PartNumber);
        Assert.Equal("HP EliteBook 645", result.ModelName);
    }

    [Fact]
    public async Task OperatorModelCatalogOverridesBuiltInModelName()
    {
        var dataSource = new FakeRecognitionDataSource();
        dataSource.ModelMappings["A4TH1AV"] = "Organization EliteBook Name";

        var result = await CreateService(dataSource).RecognizeAsync(
            _parser.Parse(">«RS»06«GS»18S7ESQ72MQ5390WTS"));

        Assert.Equal("A4TH1AV", result.PartNumber);
        Assert.Equal("Organization EliteBook Name", result.ModelName);
        Assert.Equal("Operator model catalog", result.Source);
    }

    [Fact]
    public async Task ReusesOnlyConsistentExactSerialHistory()
    {
        var dataSource = new FakeRecognitionDataSource();
        dataSource.History.AddRange(
        [
            new DeviceIdentityHistoryItem
            {
                PartNumber = "HISTORY-PART",
                ModelName = "Historical Model",
                RawScanValue = "[)>0618S7ESQ7HISTORY01"
            },
            new DeviceIdentityHistoryItem
            {
                PartNumber = "history-part",
                ModelName = "Historical Model",
                RawScanValue = "[)>\u001e06\u001d18S7ESQ7HISTORY01"
            }
        ]);

        var result = await CreateService(dataSource).RecognizeAsync(
            _parser.Parse("[)>0618S7ESQ7HISTORY01"));

        Assert.Equal("HISTORY-PART", result.PartNumber);
        Assert.Equal("Historical Model", result.ModelName);
        Assert.Equal("Exact serial history", result.Source);
        Assert.Equal("HISTORY01", dataSource.RequestedSerial);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConflictingHistoryIsNotReused(bool conflictIsPartNumber)
    {
        var dataSource = new FakeRecognitionDataSource();
        dataSource.History.AddRange(
        [
            new DeviceIdentityHistoryItem
            {
                PartNumber = "PART-A",
                ModelName = "Model A",
                RawScanValue = "[)>0618S7ESQ7CONFLICT01"
            },
            new DeviceIdentityHistoryItem
            {
                PartNumber = conflictIsPartNumber ? "PART-B" : "PART-A",
                ModelName = conflictIsPartNumber ? "Model A" : "Model B",
                RawScanValue = "[)>0618S7ESQ7CONFLICT01"
            }
        ]);

        var result = await CreateService(dataSource).RecognizeAsync(
            _parser.Parse("[)>0618S7ESQ7CONFLICT01"));

        Assert.Equal(string.Empty, result.PartNumber);
        Assert.Equal(string.Empty, result.ModelName);
    }

    [Fact]
    public async Task HistoryWithDifferentKnownCageIsNotReused()
    {
        var dataSource = new FakeRecognitionDataSource();
        dataSource.History.Add(new DeviceIdentityHistoryItem
        {
            PartNumber = "OTHER-MANUFACTURER-PART",
            ModelName = "Other Manufacturer Model",
            RawScanValue = "[)>0618SABCDECOLLIDE01"
        });

        var result = await CreateService(dataSource).RecognizeAsync(
            _parser.Parse("[)>0618S7ESQ7COLLIDE01"));

        Assert.Equal(string.Empty, result.PartNumber);
        Assert.Equal(string.Empty, result.ModelName);
    }

    private DeviceRecognitionService CreateService(IDeviceRecognitionDataSource dataSource) =>
        new(dataSource, _parser, new KnownDeviceCatalog());

    private sealed class FakeRecognitionDataSource : IDeviceRecognitionDataSource
    {
        public Dictionary<string, string> ModelMappings { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<DeviceIdentityHistoryItem> History { get; } = [];

        public string RequestedSerial { get; private set; } = string.Empty;

        public Task<string?> ResolveModelNameAsync(
            string? partNumber,
            CancellationToken cancellationToken = default)
        {
            var part = partNumber?.Trim() ?? string.Empty;
            return Task.FromResult(
                ModelMappings.TryGetValue(part, out var modelName)
                    ? modelName
                    : null);
        }

        public Task<IReadOnlyList<DeviceIdentityHistoryItem>> GetDeviceIdentityHistoryAsync(
            string? serialNumber,
            CancellationToken cancellationToken = default)
        {
            RequestedSerial = serialNumber?.Trim() ?? string.Empty;
            return Task.FromResult<IReadOnlyList<DeviceIdentityHistoryItem>>(History);
        }
    }
}
