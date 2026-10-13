using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Core.Tests.Authentication;

/// <summary>
///     What a live session counts as the same user with the same rights: every claim but the ones a token carries
///     about itself.
/// </summary>
public class PrincipalRightsTests
{
    [Fact]
    public void A_token_issued_again_with_the_same_claims_is_the_same_user()
    {
        var first = Principal(("sub", "alice"), ("role", "admin"), ("exp", "100"), ("iat", "1"), ("jti", "a"));
        var refreshed = Principal(("sub", "alice"), ("role", "admin"), ("exp", "900"), ("iat", "800"), ("jti", "b"));

        var same = PrincipalRights.Same(first, refreshed);

        Assert.True(same);
    }

    [Theory]
    [InlineData("role", "user")]
    [InlineData("rask:tenant", "7")]
    [InlineData("sub", "bob")]
    public void A_claim_that_says_who_or_what_they_may_do_is_a_change(string type, string value)
    {
        var before = Principal(("sub", "alice"), ("role", "admin"), ("rask:tenant", "3"));
        var after = Principal([.. before.Claims.Where(c => c.Type != type).Select(c => (c.Type, c.Value)), (type, value)]);

        var same = PrincipalRights.Same(before, after);

        Assert.False(same);
    }

    [Fact]
    public void The_order_the_claims_are_listed_in_does_not_matter()
    {
        var one = Principal(("sub", "alice"), ("role", "admin"), ("role", "editor"));
        var other = Principal(("role", "editor"), ("sub", "alice"), ("role", "admin"));

        var same = PrincipalRights.Same(one, other);

        Assert.True(same);
    }

    [Fact]
    public void A_claim_held_twice_is_not_the_claim_held_once_beside_another()
    {
        var twice = Principal(("sub", "alice"), ("role", "admin"), ("role", "admin"));
        var mixed = Principal(("sub", "alice"), ("role", "admin"), ("role", "editor"));

        var same = PrincipalRights.Same(twice, mixed);

        Assert.False(same);
    }

    [Fact]
    public void A_removed_claim_is_a_change()
    {
        var before = Principal(("sub", "alice"), ("role", "admin"));
        var after = Principal(("sub", "alice"));

        var same = PrincipalRights.Same(before, after);

        Assert.False(same);
    }

    [Fact]
    public void Somebody_with_no_claims_is_not_nobody()
    {
        var somebody = new ClaimsPrincipal(new ClaimsIdentity([], "Test"));
        var nobody = new ClaimsPrincipal(new ClaimsIdentity());

        var same = PrincipalRights.Same(somebody, nobody);

        Assert.False(same);
    }

    [Fact]
    public void One_principal_compared_with_itself_allocates_nothing()
    {
        var principal = Principal(("sub", "alice"), ("role", "admin"));
        PrincipalRights.Same(principal, principal);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var same = PrincipalRights.Same(principal, principal);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(same);
        Assert.Equal(0, allocated);
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "Test"));
}
