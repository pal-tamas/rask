using System.Security.Claims;
using Rask.Server.Authentication;

namespace Rask.Server.Tests.Authentication;

public class SessionUserProviderTests
{
    [Fact]
    public void Clearing_after_a_sign_in_resets_to_anonymous_and_raises_Changed()
    {
        var provider = new SessionUserProvider();
        var changes = 0;
        provider.Changed += () => changes++;

        provider.Set(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "test")));

        Assert.True(provider.Current.Identity?.IsAuthenticated);
        Assert.Equal(1, changes);

        provider.Clear();

        Assert.False(provider.Current.Identity?.IsAuthenticated ?? false);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Clearing_an_already_anonymous_user_does_nothing()
    {
        var provider = new SessionUserProvider();
        var changes = 0;
        provider.Changed += () => changes++;

        provider.Clear();

        Assert.False(provider.Current.Identity?.IsAuthenticated ?? false);
        Assert.Equal(0, changes);
    }
}
