using System.Security.Claims;
using Rask.Server.Authentication;

namespace Rask.Server.Tests.Authentication;

public class SessionUserProviderTests
{
    [Fact]
    public void Clear_AfterSignIn_ResetsToAnonymous_AndRaisesChanged()
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
    public void Clear_WhenAlreadyAnonymous_IsNoOp()
    {
        var provider = new SessionUserProvider();
        var changes = 0;
        provider.Changed += () => changes++;

        provider.Clear();

        Assert.False(provider.Current.Identity?.IsAuthenticated ?? false);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void ReadCount_CountsReadsOfCurrent_NotTheFrameworkReplacingIt()
    {
        // The count answers "did this render ask who the user is". Seeding or clearing the principal is
        // the framework changing it, and counting that would mark every page as depending on the user.
        var provider = new SessionUserProvider();

        provider.Set(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "test")));
        provider.Clear();
        Assert.Equal(0, provider.ReadCount);

        _ = provider.Current;
        _ = provider.Current;
        Assert.Equal(2, provider.ReadCount);
    }
}
