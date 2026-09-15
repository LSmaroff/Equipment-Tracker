using EquipmentTracking.App.Services;

namespace EquipmentTracking.Tests;

public sealed class BarcodeParserTests
{
    private readonly BarcodeParser _parser = new();

    [Fact]
    public void Parses_KeyValue_Scan()
    {
        var result = _parser.Parse("MODEL=Latitude 7450;SN=ABC12345;ASSET=58SOW001");

        Assert.True(result.Parsed);
        Assert.Equal("Latitude 7450", result.PartNumber);
        Assert.Equal("ABC12345", result.SerialNumber);
        Assert.Equal("58SOW001", result.AssetTag);
    }

    [Fact]
    public void Parses_Delimited_ThreePart_Scan()
    {
        var result = _parser.Parse("EliteBook 840|SER123|TAG456");

        Assert.True(result.Parsed);
        Assert.Equal("EliteBook 840", result.PartNumber);
        Assert.Equal("SER123", result.SerialNumber);
        Assert.Equal("TAG456", result.AssetTag);
    }

    [Theory]
    [InlineData("[)>0617V1PWX71PAC-MSTNGT630022S1391590010156 ", "AC-MSTNGT630022", "1391590010156")]
    [InlineData("[)>0617V1PWX71PAC-MSTNGT630022S1391590010130", "AC-MSTNGT630022", "1391590010130")]
    [InlineData("[)>0617V3DMD31P210-BGNWS4SX7KZ3", "210-BGNW", "4SX7KZ3")]
    public void Parses_Compact_Dod_Iuid_And_Ignores_Cage_Code(
        string scan,
        string expectedPart,
        string expectedSerial)
    {
        var result = _parser.Parse(scan);

        Assert.True(result.Parsed);
        Assert.Equal(expectedPart, result.PartNumber);
        Assert.Equal(expectedSerial, result.SerialNumber);
        Assert.Equal(scan, result.RawValue);
    }

    [Fact]
    public void Parses_Dod_Iuid_With_Control_Separators()
    {
        const string scan = "[)>\u001e06\u001d17V1PWX7\u001d1PAC-MSTNGT630022\u001dS1391590010156\u001e\u0004";

        var result = _parser.Parse(scan);

        Assert.True(result.Parsed);
        Assert.Equal("AC-MSTNGT630022", result.PartNumber);
        Assert.Equal("1391590010156", result.SerialNumber);
        Assert.Equal(scan, result.RawValue);
    }

    [Theory]
    [InlineData("[)>0618S7ESQ72TK24704SR", "2TK24704SR")]
    [InlineData("[)>½RSW06½GSW18S7ESQ72MQ5390W75", "2MQ5390W75")]
    public void Parses_18S_Cage_And_Serial_Scans(
        string scan,
        string expectedSerial)
    {
        var result = _parser.Parse(scan);

        Assert.True(result.Parsed);
        Assert.Equal(string.Empty, result.PartNumber);
        Assert.Equal(expectedSerial, result.SerialNumber);
        Assert.Equal(scan, result.RawValue);
        Assert.Contains("part number manually", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[)>0617V3DMD31P210-AVUYSFTB0C93", "210-AVUY", "FTB0C93")]
    [InlineData("[)>Transource 17V0MM09S74014593W1PPMM32U-0FK00LTA♥", "PMM32U-0FK00LTA", "74014593W")]
    [InlineData("\\0000[)>RS06«GS»18S7ESQ72MQ5390VY3", "", "2MQ5390VY3")]
    [InlineData("[)>0617V1PWX71PAC-MSTNGT630022S1391590010170", "AC-MSTNGT630022", "1391590010170")]
    [InlineData("[)>0618S7ESQ72TK2270CJM", "", "2TK2270CJM")]
    [InlineData("[)♥0618S7ESQ72TK24101NR", "", "2TK24101NR")]
    [InlineData("[)0617V1PWX71PPXY-00001S010959402253", "PXY-00001", "010959402253")]
    [InlineData("0617V0PF981P5262GA71C01FSRQA03V1992", "5262GA71C01F", "RQA03V1992")]
    public void Parses_Field_Scanner_Samples(
        string scan,
        string expectedPart,
        string expectedSerial)
    {
        var result = _parser.Parse(scan);

        Assert.True(result.Parsed);
        Assert.Equal(expectedPart, result.PartNumber);
        Assert.Equal(expectedSerial, result.SerialNumber);
        Assert.Equal(scan, result.RawValue);
    }

    [Fact]
    public void Unknown_Scan_Is_Preserved()
    {
        var result = _parser.Parse("UNRECOGNIZEDVALUE");

        Assert.False(result.Parsed);
        Assert.Equal("UNRECOGNIZEDVALUE", result.RawValue);
    }
}
