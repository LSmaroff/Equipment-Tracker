using EquipmentTracking.App.Services;

namespace EquipmentTracking.Tests;

public sealed class DashboardSearchInputRouterTests
{
    [Theory]
    [InlineData("E")]
    [InlineData("Model 123")]
    [InlineData("ETP1297:TX-20260810-143025-1A2B3C4D")]
    public void TryCreateInitialQuery_AcceptsPrintableTypingAndScannerText(string input)
    {
        var accepted = DashboardSearchInputRouter.TryCreateInitialQuery(
            input,
            commandModifierPressed: false,
            focusedControlOwnsTextInput: false,
            out var query);

        Assert.True(accepted);
        Assert.Equal(input, query);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\r")]
    public void TryCreateInitialQuery_RejectsEmptyWhitespaceAndControlInput(string? input)
    {
        Assert.False(DashboardSearchInputRouter.TryCreateInitialQuery(
            input,
            commandModifierPressed: false,
            focusedControlOwnsTextInput: false,
            out var query));
        Assert.Equal(string.Empty, query);
    }

    [Fact]
    public void TryCreateInitialQuery_PreservesApplicationShortcuts()
    {
        Assert.False(DashboardSearchInputRouter.TryCreateInitialQuery(
            "f",
            commandModifierPressed: true,
            focusedControlOwnsTextInput: false,
            out _));
    }

    [Fact]
    public void TryCreateInitialQuery_PreservesFocusedTextAndComboBoxInput()
    {
        Assert.False(DashboardSearchInputRouter.TryCreateInitialQuery(
            "A",
            commandModifierPressed: false,
            focusedControlOwnsTextInput: true,
            out _));
    }

    [Fact]
    public void TryCreateInitialQuery_EnforcesDashboardSearchLength()
    {
        var input = new string('A', DashboardSearchInputRouter.MaximumQueryLength + 20);

        Assert.True(DashboardSearchInputRouter.TryCreateInitialQuery(
            input,
            commandModifierPressed: false,
            focusedControlOwnsTextInput: false,
            out var query));
        Assert.Equal(DashboardSearchInputRouter.MaximumQueryLength, query.Length);
    }
}
