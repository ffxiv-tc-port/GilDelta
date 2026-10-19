using GilDelta.Characters;

namespace GilDelta.Tests.Characters;

public class LoginSummaryGateTests
{
    [Fact]
    public void Summary_is_new_once_per_character_until_it_changes()
    {
        var gate = new LoginSummaryGate();

        Assert.True(gate.IsNew(1, "A|chest|False"));
        Assert.False(gate.IsNew(1, "A|chest|False"));
        Assert.True(gate.IsNew(2, "A|chest|False"));
        Assert.True(gate.IsNew(1, "A|other|False"));
        Assert.True(gate.IsNew(1, "A|chest|False"));
        Assert.False(gate.IsNew(2, "A|chest|False"));
    }
}
