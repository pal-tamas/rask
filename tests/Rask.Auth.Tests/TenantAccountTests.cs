using Microsoft.EntityFrameworkCore;
using Rask.Data;

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

    // Written through EF rather than a factory: the credentials have private setters and Register is
    // internal to Rask.Auth, which is the point of them — only the auth flows mint an account.
    private static async Task AddAsync(AuthHarness harness, string email, Guid? tenant)
    {
        await using var db = harness.NewContext();
        var user = new TestUser();

        db.Add(user);

        var entry = db.Entry(user);
        entry.Property(nameof(Authenticatable.Id)).CurrentValue = Guid.CreateVersion7();
        entry.Property(nameof(Authenticatable.Email)).CurrentValue = Authenticatable.NormalizeEmail(email);
        entry.Property("PasswordHash").CurrentValue = "x";

        if (tenant is { } id)
        {
            entry.Property(Columns.TenantId).CurrentValue = id;
        }

        await db.SaveChangesAsync();
    }
}
