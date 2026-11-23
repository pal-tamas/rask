using System.Security.Claims;

namespace Rask.Data.Tests;

/// <summary>
/// <see cref="Current" /> answers "who is this work for" from anywhere — a static factory, a read — with nothing
/// injected, because the host makes the session's or request's services ambient around the work.
/// </summary>
public sealed class CurrentTests
{
    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();
    private readonly Guid _acme = Guid.NewGuid();
    private readonly Guid _globex = Guid.NewGuid();

    [Fact]
    public void Outside_any_session_or_request_there_is_nobody_and_the_required_forms_throw()
    {
        Assert.Null(Current.Principal);
        Assert.Null(Current.UserId);
        Assert.Null(Tenant.Key);
        Assert.Throws<InvalidOperationException>(() => Current.RequiredUserId);
        Assert.Throws<InvalidOperationException>(() => Tenant.RequiredKey);
    }

    [Fact]
    public void Inside_a_scope_the_signed_in_principal_answers()
    {
        var principal = SignedIn(_alice, _acme);

        using (Db.UseScope(new SessionScope(principal)))
        {
            Assert.Same(principal, Current.Principal);
            Assert.Equal(_alice, Current.RequiredUserId);
            Assert.Equal(_acme, Tenant.RequiredKey);
        }

        Assert.Null(Current.UserId);
    }

    [Fact]
    public void UsePrincipal_answers_for_the_principal_its_user_and_its_tenant_until_disposed()
    {
        var principal = SignedIn(_alice, _acme);

        using (Current.UsePrincipal(principal))
        {
            Assert.Same(principal, Current.Principal);
            Assert.Equal(_alice, Current.RequiredUserId);
            Assert.Equal(_acme, Tenant.RequiredKey);
        }

        Assert.Null(Current.Principal);
        Assert.Null(Current.UserId);
        Assert.Null(Tenant.Key);
    }

    [Fact]
    public void UsePrincipal_beats_the_principal_of_the_scope_and_gives_it_back_on_dispose()
    {
        var session = SignedIn(_alice, _acme);
        var other = SignedIn(_bob, _globex);
        using var scope = Db.UseScope(new SessionScope(session));

        Guid? user;
        Guid? tenant;
        using (Current.UsePrincipal(other))
        {
            user = Current.UserId;
            tenant = Tenant.Key;
        }

        Assert.Equal(_bob, user);
        Assert.Equal(_globex, tenant);
        Assert.Same(session, Current.Principal);
    }

    [Fact]
    public async Task UsePrincipal_follows_an_await_and_nests()
    {
        using (Current.UsePrincipal(SignedIn(_alice, _acme)))
        {
            await Task.Yield();
            Assert.Equal(_alice, Current.UserId);

            using (Current.UsePrincipal(SignedIn(_bob, _globex)))
            {
                Assert.Equal(_bob, Current.UserId);
            }

            Assert.Equal(_alice, Current.UserId);
        }

        Assert.Null(Current.UserId);
    }

    [Fact]
    public void An_anonymous_principal_has_no_user_id()
    {
        // What a session holds before sign-in: a principal, never null, with no identity claims.
        using (Current.UsePrincipal(new ClaimsPrincipal(new ClaimsIdentity())))
        {
            Assert.NotNull(Current.Principal);
            Assert.Null(Current.UserId);
        }
    }

    [Fact]
    public void A_signed_in_user_s_first_claim_and_roles_answer_with_nothing_injected()
    {
        var principal = SignedIn(_alice, _acme,
            new Claim("department", "sales"),
            new Claim("department", "support"),
            new Claim(ClaimTypes.Role, "Admin"));

        using var scope = Current.UsePrincipal(principal);
        var department = Current.Claim("department");
        var missing = Current.Claim("nickname");
        var admin = Current.IsInRole("Admin");
        var clerk = Current.IsInRole("Clerk");

        Assert.Equal("sales", department);
        Assert.Null(missing);
        Assert.True(admin);
        Assert.False(clerk);
    }

    [Fact]
    public void An_anonymous_principal_has_no_claims_to_read_and_is_in_no_role_whatever_it_carries()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("department", "sales"), new Claim(ClaimTypes.Role, "Admin")]));

        using var scope = Current.UsePrincipal(anonymous);
        var department = Current.Claim("department");
        var admin = Current.IsInRole("Admin");

        Assert.Equal("sales", department);
        Assert.False(admin);
    }

    [Fact]
    public void Outside_any_session_or_request_there_is_no_claim_and_no_role()
    {
        var department = Current.Claim("department");
        var admin = Current.IsInRole("Admin");

        Assert.Null(department);
        Assert.False(admin);
    }

    [Fact]
    public void A_blank_claim_type_or_role_is_refused()
    {
        var principal = SignedIn(_alice, _acme);

        using var scope = Current.UsePrincipal(principal);

        Assert.Throws<ArgumentException>(() => Current.Claim(" "));
        Assert.Throws<ArgumentException>(() => Current.IsInRole(""));
    }

    [Fact]
    public void UseUser_beats_the_signed_in_user_and_leaves_the_principal_alone()
    {
        var principal = SignedIn(_alice, _acme);

        // A job runs for the user its row recorded, whoever is ambient. Nobody signed in to run it, so the
        // principal is not rewritten to pretend otherwise.
        using (Current.UsePrincipal(principal))
        using (Current.UseUser(_bob))
        {
            Assert.Equal(_bob, Current.UserId);
            Assert.Same(principal, Current.Principal);
        }
    }

    [Fact]
    public async Task UseUser_follows_an_await_and_restores_on_dispose()
    {
        using (Current.UseUser(_alice))
        {
            await Task.Yield();
            Assert.Equal(_alice, Current.UserId);

            using (Current.UseUser(null))
            {
                Assert.Null(Current.UserId);
            }

            Assert.Equal(_alice, Current.UserId);
        }

        Assert.Null(Current.UserId);
    }

    [Fact]
    public void An_explicit_tenant_beats_the_principal_and_Across_and_None_are_nobodys()
    {
        using (Current.UsePrincipal(SignedIn(_alice, _acme)))
        {
            using (Tenant.Use(_globex))
            {
                Assert.Equal(_globex, Tenant.Key);
            }

            // None clears the tenant, the signed-in user's included.
            using (Tenant.None())
            {
                Assert.Null(Tenant.Key);
                Assert.Throws<InvalidOperationException>(() => Tenant.RequiredKey);
            }

            using (Tenant.Across())
            {
                Assert.Null(Tenant.Key);
                Assert.Throws<InvalidOperationException>(() => Tenant.RequiredKey);
            }
        }
    }

    private static ClaimsPrincipal SignedIn(Guid userId, Guid tenant, params Claim[] more) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(Tenant.ClaimType, tenant.ToString()),
            .. more,
        ], "Test"));

    // What a session or request holds: its own principal, through the scope the host made ambient.
    private sealed class SessionScope(ClaimsPrincipal principal) : IServiceProvider, IPrincipalSource
    {
        public ClaimsPrincipal? Current => principal;

        public object? GetService(Type serviceType) => serviceType == typeof(IPrincipalSource) ? this : null;
    }
}
