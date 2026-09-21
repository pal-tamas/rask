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
        Assert.Null(Current.Tenant);
        Assert.Throws<InvalidOperationException>(() => Current.RequiredUserId);
        Assert.Throws<InvalidOperationException>(() => Current.RequiredTenant);
    }

    [Fact]
    public void Inside_a_scope_the_signed_in_principal_answers()
    {
        var principal = SignedIn(_alice, _acme);

        using (Db.UseScope(new StubScope(principal)))
        {
            Assert.Same(principal, Current.Principal);
            Assert.Equal(_alice, Current.RequiredUserId);
            Assert.Equal(_acme, Current.RequiredTenant);
        }

        Assert.Null(Current.UserId);
    }

    [Fact]
    public void An_anonymous_principal_has_no_user_id()
    {
        // What a session holds before sign-in: a principal, never null, with no identity claims.
        using (Db.UseScope(new StubScope(new ClaimsPrincipal(new ClaimsIdentity()))))
        {
            Assert.NotNull(Current.Principal);
            Assert.Null(Current.UserId);
        }
    }

    [Fact]
    public void UseUser_beats_the_signed_in_user_and_leaves_the_principal_alone()
    {
        var principal = SignedIn(_alice, _acme);

        // A job runs for the user its row recorded, whoever is ambient. Nobody signed in to run it, so the
        // principal is not rewritten to pretend otherwise.
        using (Db.UseScope(new StubScope(principal)))
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
    public void An_explicit_tenant_beats_the_principal_and_Across_is_nobodys()
    {
        using (Db.UseScope(new StubScope(SignedIn(_alice, _acme))))
        {
            using (Tenant.Use(_globex))
            {
                Assert.Equal(_globex, Current.Tenant);
            }

            // None clears the explicit scope only; the signed-in user still says which tenant.
            using (Tenant.None())
            {
                Assert.Equal(_acme, Current.Tenant);
            }

            using (Tenant.Across())
            {
                Assert.Null(Current.Tenant);
                Assert.Throws<InvalidOperationException>(() => Current.RequiredTenant);
            }
        }
    }

    private static ClaimsPrincipal SignedIn(Guid userId, Guid tenant) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(Tenant.ClaimType, tenant.ToString()),
        ], "Test"));

    private sealed class StubScope(ClaimsPrincipal principal) : IServiceProvider, IPrincipalSource
    {
        public ClaimsPrincipal? Current => principal;

        public object? GetService(Type serviceType) => serviceType == typeof(IPrincipalSource) ? this : null;
    }
}
