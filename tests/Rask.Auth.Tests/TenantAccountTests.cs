using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;
using Rask.Wire;

namespace Rask.Auth.Tests;

/// <summary>
/// An address is unique WITHIN a tenant, so the same person can hold an account at two companies — and an
/// administrator, who belongs to no tenant, still cannot share an address with another administrator.
/// </summary>
/// <remarks>
/// <para>
/// The accounts table maps its tenant itself rather than declaring <c>Scope = Tenancy.PerTenant</c>. It has
/// to: a tenant-scoped row is stamped from the ambient tenant and refused without one, and an administrator
/// legitimately has none — a <c>PerTenant</c> accounts table could not have an admin inserted into it at all.
/// </para>
/// <para>
/// The unique index is on <c>TenantKey</c>, not <c>TenantId</c>, because NULL in a unique index is not
/// portable. SQLite and PostgreSQL treat two NULLs as distinct, so any number of admins could share one
/// address; SQL Server treats them as equal, so only one could. Folding the null to <c>Guid.Empty</c> makes
/// one ordinary index behave the same on all three.
/// </para>
/// </remarks>
[Collection(AuthDbCollection.Name)]
public sealed class TenantAccountTests
{
    private readonly Guid _acme = Guid.NewGuid();
    private readonly Guid _globex = Guid.NewGuid();

    [Fact]
    public async Task The_same_address_can_hold_an_account_in_two_tenants()
    {
        await using var harness = new AuthHarness();

        await AddAsync(harness, "ada@example.com", _acme);
        await AddAsync(harness, "ada@example.com", _globex);

        await using var db = harness.NewContext();
        Assert.Equal(2, await db.Set<TestUser>().CountAsync(u => u.Email == "ada@example.com"));
    }

    [Fact]
    public async Task The_same_address_twice_in_one_tenant_is_refused_by_the_database()
    {
        await using var harness = new AuthHarness();

        await AddAsync(harness, "ada@example.com", _acme);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => AddAsync(harness, "ada@example.com", _acme));
    }

    [Fact]
    public async Task Two_administrators_cannot_share_an_address_even_though_neither_has_a_tenant()
    {
        await using var harness = new AuthHarness();

        // Both have a null tenant. Left as NULL in the index this passes on SQLite and PostgreSQL and fails
        // on SQL Server — the divergence TenantKey exists to remove.
        await AddAsync(harness, "root@example.com", tenant: null);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => AddAsync(harness, "root@example.com", tenant: null));
    }

    [Fact]
    public async Task An_account_with_no_tenant_still_saves()
    {
        await using var harness = new AuthHarness();

        // The case a PerTenant accounts table could not serve at all: no ambient tenant, and the insert has
        // to succeed rather than be refused for having none.
        await AddAsync(harness, "root@example.com", tenant: null);

        await using var db = harness.NewContext();
        var admin = await db.Set<TestUser>().SingleAsync();

        Assert.Null(admin.TenantId);
        Assert.Equal(Guid.Empty, db.Entry(admin).Property<Guid>(Columns.TenantKey).CurrentValue);
    }

    [Fact]
    public async Task A_signed_in_user_carries_their_tenant_on_the_principal()
    {
        await using var harness = new AuthHarness();
        await AddAsync(harness, "ada@example.com", _acme);

        await using var db = harness.NewContext();
        var user = await db.Set<TestUser>().SingleAsync();

        // The tenant is an OUTCOME of authentication, not an input to routing: sign-in finds the user, and
        // the user says which tenant they are in. So it rides on the principal, and the data layer reads it
        // back off the claim rather than being handed it.
        var principal = AuthPrincipal.For(user);

        Assert.Equal(_acme, AuthPrincipal.TenantId(principal));
        Assert.Equal(_acme.ToString(), principal.FindFirst(Tenant.ClaimType)?.Value);
    }

    [Fact]
    public async Task An_administrator_carries_no_tenant_claim()
    {
        await using var harness = new AuthHarness();
        await AddAsync(harness, "root@example.com", tenant: null);

        await using var db = harness.NewContext();
        var admin = await db.Set<TestUser>().SingleAsync();

        // No claim, so a tenant-scoped read throws until they choose a tenant to work in. That is the
        // intended behaviour: an admin should say which tenant they are acting in, not silently read across.
        var principal = AuthPrincipal.For(admin);

        Assert.Null(AuthPrincipal.TenantId(principal));
        Assert.Null(principal.FindFirst(Tenant.ClaimType));
    }

    [Fact]
    public async Task Signing_in_with_an_address_two_tenants_hold_is_refused_not_guessed()
    {
        await using var harness = new AuthHarness();

        // Sign-in cannot filter by tenant: it has to find the user BEFORE it can know which tenant they are
        // in. With the same address in two tenants an unordered FirstOrDefault would sign somebody into
        // whichever row came back first — non-deterministic, and the wrong tenant half the time.
        await AddAsync(harness, "ada@example.com", _acme, password: Password);
        await AddAsync(harness, "ada@example.com", _globex, password: Password);

        Assert.NotEqual(AuthResult.Success, await SignInAsync(harness, "ada@example.com"));
    }

    [Fact]
    public async Task One_account_with_that_address_still_signs_in()
    {
        await using var harness = new AuthHarness();
        await AddAsync(harness, "ada@example.com", _acme, password: Password);

        // The guard must not break the ordinary case — one match is still one match.
        Assert.Equal(AuthResult.Success, await SignInAsync(harness, "ada@example.com"));
    }

    // Written through EF rather than a factory: the credentials have private setters and Register is
    // internal to Rask.Auth, which is the point of them — only the auth flows mint an account.
    private const string Password = "correct horse battery";

    private static async Task<AuthResult> SignInAsync(AuthHarness harness, string email)
    {
        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<TestUser>>();
        return (await accounts.ValidateAsync(email, Password, null)).Result;
    }

    private static async Task AddAsync(AuthHarness harness, string email, Guid? tenant, string? password = null)
    {
        await using var db = harness.NewContext();
        var user = new TestUser();

        db.Add(user);

        var entry = db.Entry(user);
        entry.Property(nameof(Authenticatable.Id)).CurrentValue = Guid.CreateVersion7();
        entry.Property(nameof(Authenticatable.Email)).CurrentValue = Authenticatable.NormalizeEmail(email);
        entry.Property("PasswordHash").CurrentValue =
            password is null ? "x" : new PasswordHasher().Hash(password);
        entry.Property(nameof(Authenticatable.EmailConfirmedAt)).CurrentValue = DateTime.UtcNow;

        if (tenant is { } id)
        {
            entry.Property(Columns.TenantId).CurrentValue = id;
        }

        await db.SaveChangesAsync();
    }
}
