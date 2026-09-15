using EquipmentTracking.App.Models;

namespace EquipmentTracking.Tests;

public sealed class CustomerIdentityTests
{
    [Fact]
    public void DisplayName_Uses_Rank_Last_Comma_First_Middle()
    {
        var identity = new CustomerIdentity
        {
            Rank = "A1C",
            FirstName = "Liam",
            MiddleInitial = "D",
            LastName = "Smaroff"
        };

        Assert.Equal("A1C Smaroff, Liam D", identity.DisplayName);
    }
}
