using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// A user numbered by the app and kept per tenant — Eszaf's shape. It does not say IUser: the test names it to
// CurrentRows exactly as the generated code would.
public sealed class Teller : Aggregate<int, Model, PerTenant<int>>
{
    private Teller() { }

    public string Name { get; private set; } = "";

    public static Teller Hire(int id, string name) => new() { Id = id, Name = name };
}

/// <summary>
/// The generated <c>Current.UserId</c> and <c>Current.TenantId</c> of an app whose user and tenant are keyed by a
/// number: the claim names the number, the framework carries it as a Guid, and the app reads its own number back.
/// </summary>
public sealed class NumberedCurrentIdTests : IDisposable
{
    private static readonly Guid Twelve = new("00000000-0000-0000-0000-00000000000c");

    private readonly InMemorySqlite _sqlite = new();

    public void Dispose()
    {
        CurrentRows.Reset();
        _sqlite.Dispose();
    }

    [Fact]
    public void A_numeric_name_identifier_is_user_12_in_an_app_whose_users_are_numbered()
    {
        NumberedUsers();

        int? number;
        Guid? carried;
        using (Current.UsePrincipal(SignedIn("12")))
        {
            number = CurrentRows.UserId<int>();
            carried = CurrentUser.Id;
        }

        Assert.Equal(12, number);
        Assert.Equal(Twelve, carried);
    }

    [Fact]
    public void A_numeric_name_identifier_is_nobody_in_an_app_whose_users_are_Guids()
    {
        Use(CurrentRowKind.User, typeof(Member), static (Guid key) => (MemberRead row) => row.Id == key, static (MemberRead row) => row.Id);

        Guid? read;
        using (Current.UsePrincipal(SignedIn("12")))
        {
            read = Current.UserId;
        }

        Assert.Null(read);
    }

    [Fact]
    public void Use_user_by_number_makes_that_user_current_until_disposed()
    {
        NumberedUsers();

        int? inside, nested, after;
        Guid? carried;
        using (CurrentRows.WorkFor<int>(12))
        {
            inside = CurrentRows.UserId<int>();
            carried = Current.UserId;
            using (CurrentRows.WorkFor<int>(null))
            {
                nested = CurrentRows.UserId<int>();
            }
        }

        after = CurrentRows.UserId<int>();

        Assert.Equal((12, (int?)null, (int?)null), (inside, nested, after));
        Assert.Equal(Twelve, carried);
    }

    [Fact]
    public void Required_user_id_throws_when_nobody_is_signed_in()
    {
        NumberedUsers();

        var refused = Assert.Throws<InvalidOperationException>(() => CurrentRows.RequiredUserId<int>());

        Assert.Contains("Current.UseUser(id)", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_user_that_carries_no_number_throws_naming_the_call_that_does()
    {
        NumberedUsers();

        InvalidOperationException refused;
        using (Current.UseUser(Guid.CreateVersion7()))
        {
            refused = Assert.Throws<InvalidOperationException>(() => CurrentRows.UserId<int>());
        }

        Assert.Contains("Teller is keyed by Int32", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Current.UseUser(42)", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_number_past_an_int_throws_rather_than_wrapping()
    {
        NumberedUsers();

        using (CurrentRows.WorkFor<long>(int.MaxValue + 1L))
        {
            Assert.Throws<InvalidOperationException>(() => CurrentRows.UserId<int>());
        }
    }

    [Fact]
    public void The_tenant_id_is_the_number_inside_its_scope_and_nothing_across_tenants()
    {
        Use(CurrentRowKind.Tenant, typeof(NumberShop), static (int key) => (NumberShopRead row) => row.Id == key, static (NumberShopRead row) => row.Id);

        int? inside, required, across, outside;
        InvalidOperationException acrossRefused;
        using (Tenant.Use(12))
        {
            inside = CurrentRows.TenantId<int>();
            required = CurrentRows.RequiredTenantId<int>();
        }

        using (Tenant.Across())
        {
            across = CurrentRows.TenantId<int>();
            acrossRefused = Assert.Throws<InvalidOperationException>(() => CurrentRows.RequiredTenantId<int>());
        }

        outside = CurrentRows.TenantId<int>();

        Assert.Equal((12, 12), (inside, required));
        Assert.Null(across);
        Assert.Null(outside);
        Assert.Contains("Tenant.Across()", acrossRefused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_numeric_tenant_claim_names_the_tenant_in_an_app_whose_tenants_are_numbered()
    {
        Use(CurrentRowKind.Tenant, typeof(NumberShop), static (int key) => (NumberShopRead row) => row.Id == key, static (NumberShopRead row) => row.Id);

        int? tenant;
        using (Current.UsePrincipal(SignedIn("12", new Claim(Tenant.ClaimType, "7"))))
        {
            tenant = CurrentRows.TenantId<int>();
        }

        Assert.Equal(7, tenant);
    }

    [Fact]
    public void A_job_principal_writes_the_users_number_back_as_the_claim()
    {
        NumberedUsers();

        var claim = CurrentUser.ToClaim(Twelve);
        var read = CurrentUser.FromClaim(claim);

        Assert.Equal("12", claim);
        Assert.Equal(Twelve, read);
    }

    [Fact]
    public async Task An_admin_working_in_another_tenant_still_reads_their_own_row()
    {
        await using var database = await TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString), cancellationToken: TestContext.Current.CancellationToken);
        using (Tenant.Use(1))
        {
            database.Context.Add(Teller.Hire(12, "Ada"));
            await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        database.Context.ChangeTracker.Clear();
        NumberedUsers();

        string name;
        using (Tenant.Use(2))
        using (CurrentRows.WorkFor<int>(12))
        {
            name = CurrentRows.RequiredUser<TellerRead>().Name;
        }

        Assert.Equal("Ada", name);
    }

    private static void NumberedUsers() =>
        Use(CurrentRowKind.User, typeof(Teller), static (int key) => (TellerRead row) => row.Id == key, static (TellerRead row) => row.Id);

    private static void Use<[DynamicallyAccessedMembers(DataTrimming.Entity)] TRead, TKey>(
        CurrentRowKind kind, Type aggregate, Func<TKey, System.Linq.Expressions.Expression<Func<TRead, bool>>> byKey, Func<TRead, TKey> keyOf)
        where TRead : class, IReadModel =>
        CurrentRows.Use(new CurrentRow<TRead, TKey>(kind, aggregate, byKey, keyOf));

    private static ClaimsPrincipal SignedIn(string nameIdentifier, params Claim[] more) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, nameIdentifier), .. more], "Test"));
}
