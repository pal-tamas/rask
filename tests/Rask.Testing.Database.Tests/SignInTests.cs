using System.Security.Claims;

namespace Rask.Testing.Database.Tests;

/// <summary><c>Test.SignIn(…)</c> signs a user in for the test's flow, with the claims a Rask sign-in issues.</summary>
public sealed class SignInTests
{
    private static readonly Guid Acme = Guid.Parse("0f8b6f1e-1a59-4d0e-9a3a-4c1f7b2f0a01");

    [Fact]
    public void A_signed_in_user_is_the_current_user_with_their_roles_and_claims()
    {
        using var ann = Test.SignIn("ann@x.io", roles: ["Admin"], claims: [new Claim("dept", "ops")]);

        var userId = Current.UserId;

        Assert.Equal(ann.Id, userId);
        Assert.True(Current.IsInRole("Admin"));
        Assert.False(Current.IsInRole("Owner"));
        Assert.Equal("ann@x.io", Current.Claim(ClaimTypes.Email));
        Assert.Equal("ann@x.io", Current.Principal?.Identity?.Name);
        Assert.Equal("ops", Current.Claim("dept"));
    }

    [Fact]
    public void A_signed_in_user_s_tenant_is_the_tenant_their_reads_filter_by()
    {
        using var ann = Test.SignIn("ann@x.io", tenant: Acme);

        var tenant = Tenant.Resolve();

        Assert.Equal(Acme, tenant);
    }

    [Fact]
    public void The_same_name_is_the_same_user_unless_an_id_is_given()
    {
        var row = Guid.NewGuid();

        Guid first, second, given;
        using (var a = Test.SignIn("ann@x.io")) { first = a.Id; }
        using (var b = Test.SignIn("ann@x.io")) { second = b.Id; }
        using (var c = Test.SignIn("ann@x.io", id: row)) { given = c.Id; }

        Assert.Equal(first, second);
        Assert.Equal(row, given);
    }

    [Fact]
    public void Disposing_the_scope_signs_the_user_out_again()
    {
        var ann = Test.SignIn("ann@x.io");

        ann.Dispose();

        Assert.Null(Current.UserId);
        Assert.Null(Current.Principal);
    }
}
