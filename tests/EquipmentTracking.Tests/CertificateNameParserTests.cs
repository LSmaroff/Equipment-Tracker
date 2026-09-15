using EquipmentTracking.App.Services;

namespace EquipmentTracking.Tests;

public sealed class CertificateNameParserTests
{
    private readonly CertificateNameParser _parser = new();

    [Fact]
    public void Parses_DotSeparated_CacName()
    {
        var identity = _parser.Parse("SMITH.JOHN.A.1234567890", null);

        Assert.Equal("John", identity.FirstName);
        Assert.Equal("A", identity.MiddleInitial);
        Assert.Equal("Smith", identity.LastName);
    }

    [Fact]
    public void Parses_LastCommaFirst_Format()
    {
        var identity = _parser.Parse("Smith, John A", null);

        Assert.Equal("John", identity.FirstName);
        Assert.Equal("A", identity.MiddleInitial);
        Assert.Equal("Smith", identity.LastName);
    }

    [Fact]
    public void Prefers_DistinguishedName_Components()
    {
        var identity = _parser.Parse(
            "SMITH.JOHN.A.1234567890",
            "CN=SMITH.JOHN.A.1234567890, G=John, I=A, SN=Smith, O=U.S. Government");

        Assert.Equal("John", identity.FirstName);
        Assert.Equal("A", identity.MiddleInitial);
        Assert.Equal("Smith", identity.LastName);
    }
}

public sealed class CertificateRankParserTests
{
    private readonly CertificateNameParser _parser = new();

    [Fact]
    public void Parses_Rank_From_Title_Attribute()
    {
        var identity = _parser.Parse(
            "SMAROFF.LIAM.D.1234567890",
            "CN=SMAROFF.LIAM.D.1234567890, T=A1C, G=Liam, I=D, SN=Smaroff, O=U.S. Government");

        Assert.Equal("A1C", identity.Rank);
        Assert.Equal("A1C Smaroff, Liam D", identity.DisplayName);
    }

    [Fact]
    public void Parses_Rank_From_Leading_Display_Name()
    {
        var identity = _parser.Parse("SSgt Smith, John A", null);

        Assert.Equal("SSgt", identity.Rank);
        Assert.Equal("SSgt Smith, John A", identity.DisplayName);
    }
}
