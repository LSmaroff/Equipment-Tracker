using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;

namespace EquipmentTracking.Tests;

public sealed class FileNameServiceTests
{
    [Fact]
    public void Builds_Stable_WindowsSafe_Name()
    {
        var service = new FileNameService();
        var identity = new CustomerIdentity
        {
            FirstName = "John",
            MiddleInitial = "A",
            LastName = "Smith"
        };

        var fileName = service.BuildFinalPdfName(
            identity,
            new DateTimeOffset(2026, 6, 28, 14, 35, 12, TimeSpan.Zero),
            "TX-20260628-143512-A7F3");

        Assert.Equal(
            "john.a.smith-06.28.26-143512-A7F3-1297.pdf",
            fileName);
    }

    [Theory]
    [InlineData("58 SOW", "58-sow")]
    [InlineData("../CON", "_con")]
    [InlineData("NUL", "_nul")]
    public void Normalizes_Organization_Folder_Names(string input, string expected)
    {
        var service = new FileNameService();

        Assert.Equal(expected, service.NormalizeDirectoryName(input));
    }

    [Fact]
    public void Builds_Closed_Pdf_Name()
    {
        var service = new FileNameService();

        var name = service.BuildClosedPdfName(
            @"C:\Data\john.a.smith-06.28.26-143512-A7F3-1297.pdf",
            new DateTimeOffset(2026, 7, 1, 9, 10, 11, TimeSpan.Zero));

        Assert.Equal(
            "john.a.smith-06.28.26-143512-a7f3-1297-CLOSED-07.01.26-091011.pdf",
            name);
    }
}
