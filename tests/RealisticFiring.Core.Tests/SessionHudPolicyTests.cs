using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

public class SessionHudPolicyTests
{
    [Fact]
    public void HostsOwnTheRuleAndSinglePlayerKeepsLocalChoices()
    {
        var policy = new SessionHudPolicy();
        Assert.False(policy.Hidden(false, false, true));
        Assert.False(policy.Hidden(true, false, false));
        Assert.True(policy.Hidden(true, false, true));
        Assert.False(policy.Hidden(true, false, false));
    }

    [Fact]
    public void ClientWaitsForHostThenFollowsLiveChangesAndResetsBetweenSessions()
    {
        var policy = new SessionHudPolicy();
        Assert.True(policy.Hidden(true, true, false));
        policy.Receive(false);
        Assert.False(policy.Hidden(true, true, true));
        policy.Receive(true);
        Assert.True(policy.Hidden(true, true, false));
        Assert.False(policy.Hidden(false, true, false));
        policy.Reset();
        Assert.True(policy.Hidden(true, true, false));
        policy.Receive(false);
        Assert.False(policy.Hidden(true, true, true));
    }
}
