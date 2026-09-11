using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authorization;

namespace Rask.Core.Tests.Authorization;

// Whether a page is guarded at all, read off the attributes with the guard's own walk. A copy of a page
// shared between visitors needs this answered strictly: any [Authorize] that is not cleared keeps a page out,
// whether or not an anonymous visitor would satisfy its policy today.
public class RouteAuthorizationRequirementTests
{
    [Fact]
    public void APlainChain_RequiresNothing()
    {
        Assert.False(RouteAuthorizationGuard.RequiresAuthorization([typeof(PlainLayout), typeof(PlainPage)]));
    }

    [Fact]
    public void AnEmptyChain_RequiresNothing()
    {
        Assert.False(RouteAuthorizationGuard.RequiresAuthorization([]));
    }

    [Fact]
    public void AGuardedPage_Requires()
    {
        Assert.True(RouteAuthorizationGuard.RequiresAuthorization([typeof(PlainLayout), typeof(MembersPage)]));
    }

    [Fact]
    public void AGuardedLayout_GuardsThePagesUnderIt()
    {
        Assert.True(RouteAuthorizationGuard.RequiresAuthorization([typeof(GuardedLayout), typeof(PlainPage)]));
    }

    [Fact]
    public void AllowAnonymousBelowAGuardedLayout_ClearsIt()
    {
        Assert.False(RouteAuthorizationGuard.RequiresAuthorization([typeof(GuardedLayout), typeof(OpenPage)]));
    }

    [Fact]
    public void AGuardBelowAllowAnonymous_StillCounts()
    {
        Assert.True(RouteAuthorizationGuard.RequiresAuthorization([typeof(OpenPage), typeof(AdminPage)]));
    }

    [Fact]
    public void APolicyAnAnonymousVisitorCouldSatisfy_StillRequires()
    {
        // Decided without evaluating the policy, on purpose: what a policy admits can change without the page
        // changing, and a copy made while it admitted everyone would outlive that.
        Assert.True(RouteAuthorizationGuard.RequiresAuthorization([typeof(PolicyPage)]));
    }

    private sealed class PlainLayout;

    private sealed class PlainPage;

    [Authorize]
    private sealed class GuardedLayout;

    [Authorize]
    private sealed class MembersPage;

    [AllowAnonymous]
    private sealed class OpenPage;

    [Authorize(Roles = "admin")]
    private sealed class AdminPage;

    [Authorize(Policy = "anyone")]
    private sealed class PolicyPage;
}
