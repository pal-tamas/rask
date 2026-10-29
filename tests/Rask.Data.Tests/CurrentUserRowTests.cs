using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// The user aggregate. It does not say IUser — the test names it to CurrentRows as the generated code would — so this
// assembly is free to keep other user-like aggregates.
public sealed class Member : Aggregate<Guid, Model, Deletable>
{
    private Member() { }

    public string Name { get; private set; } = "";

    public static Member Join(Guid id, string name) => new() { Id = id, Name = name };

    public void Rename(string name) => Name = name;
}

/// <summary>
/// <c>Current.User</c> is the signed-in user's own row: read by <c>Current.UserId</c>, held per user, dropped on a
/// save, and never somebody else's or a row that is gone.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class CurrentUserRowTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-current-user-{Guid.NewGuid():N}.db");
    private readonly Guid _ada = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();

    public void Dispose()
    {
        CurrentRows.Reset();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task The_user_in_flight_reads_their_own_row_and_nobody_reads_none()
    {
        await using var database = await StartAsync();

        using (Current.UseUser(_ada))
        {
            Assert.Equal("Ada", CurrentRows.User<MemberRead>()!.Name);
            Assert.Equal(_ada, CurrentRows.RequiredUser<MemberRead>().Id);
        }

        Assert.Null(CurrentRows.User<MemberRead>());
        Assert.Throws<InvalidOperationException>(() => CurrentRows.RequiredUser<MemberRead>());
    }

    [Fact]
    public async Task A_nested_user_never_sees_the_outer_users_row()
    {
        await using var database = await StartAsync();

        string outer, inner, after;
        using (Current.UseUser(_ada))
        {
            outer = CurrentRows.RequiredUser<MemberRead>().Name;
            using (Current.UseUser(_bob))
            {
                inner = CurrentRows.RequiredUser<MemberRead>().Name;
            }

            after = CurrentRows.RequiredUser<MemberRead>().Name;
        }

        Assert.Equal(("Ada", "Bob", "Ada"), (outer, inner, after));
    }

    [Fact]
    public async Task A_user_whose_row_is_gone_throws_rather_than_reading_as_nobody()
    {
        await using var database = await StartAsync();

        using (Current.UseUser(Guid.NewGuid()))
        {
            var refused = Assert.Throws<InvalidOperationException>(() => CurrentRows.User<MemberRead>());

            Assert.IsNotType<MissingTenantException>(refused);
            Assert.Contains("Current.User", refused.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_save_of_the_user_drops_the_row_held()
    {
        await using var database = await StartAsync();

        string before, after;
        using (Current.UseUser(_ada))
        {
            before = CurrentRows.RequiredUser<MemberRead>().Name;

            var me = (await Member.Find(_ada, TestContext.Current.CancellationToken))!;
            me.Rename("Ada Lovelace");
            await me.Save(cancellationToken: TestContext.Current.CancellationToken);

            after = CurrentRows.RequiredUser<MemberRead>().Name;
        }

        Assert.Equal(("Ada", "Ada Lovelace"), (before, after));
    }

    [Fact]
    public async Task Work_the_framework_starts_reads_the_user_and_the_tenant_ahead()
    {
        await using var database = await StartAsync();
        database.Context.Add(NumberShop.Open(7, "Acme"));
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        Use(CurrentRowKind.Tenant, typeof(NumberShop), static (int key) => (NumberShopRead row) => row.Id == key, static (NumberShopRead row) => row.Id);

        using (Tenant.Use(7))
        using (Current.UseUser(_ada))
        {
            var primed = CurrentRows.Prime(TestContext.Current.CancellationToken);
            await primed;
            var again = CurrentRows.Prime(TestContext.Current.CancellationToken);

            // Held now: the next read ahead — the one before every live event — completes on the spot.
            Assert.True(again.IsCompletedSuccessfully);
            Assert.Equal(("Acme", "Ada"), (CurrentRows.RequiredTenant<NumberShopRead>().Name, CurrentRows.RequiredUser<MemberRead>().Name));
        }
    }

    [Fact]
    public async Task Reading_ahead_for_a_signed_in_session_whose_rows_are_held_allocates_nothing()
    {
        await using var database = await StartAsync();
        database.Context.Add(NumberShop.Open(7, "Acme"));
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        Use(CurrentRowKind.Tenant, typeof(NumberShop), static (int key) => (NumberShopRead row) => row.Id == key, static (NumberShopRead row) => row.Id);
        var session = new SignedInScope(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, _ada.ToString()), new Claim(Tenant.ClaimType, TenantNumber.ToGuid(7).ToString())], "Test")));
        await CurrentRows.Prime(session, TestContext.Current.CancellationToken);

        // What a live session asks before every event, with both rows held: the allocation gate's exact count
        // stands on this costing nothing.
        _ = CurrentRows.Prime(session, CancellationToken.None);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var primed = CurrentRows.Prime(session, CancellationToken.None);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(primed.IsCompletedSuccessfully);
        Assert.Equal(0, allocated);
    }

    private sealed class SignedInScope(ClaimsPrincipal principal) : IServiceProvider, IPrincipalSource
    {
        public ClaimsPrincipal? Current => principal;

        public object? GetService(Type serviceType) => serviceType == typeof(IPrincipalSource) ? this : null;
    }

    private static void Use<[DynamicallyAccessedMembers(DataTrimming.Entity)] TRead, TKey>(
        CurrentRowKind kind, Type aggregate, Func<TKey, System.Linq.Expressions.Expression<Func<TRead, bool>>> byKey, Func<TRead, TKey> keyOf)
        where TRead : class, IReadModel =>
        CurrentRows.Use(new CurrentRow<TRead, TKey>(kind, aggregate, byKey, keyOf));

    private async Task<TestDatabase> StartAsync()
    {
        var database = await TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
        database.Context.AddRange(Member.Join(_ada, "Ada"), Member.Join(_bob, "Bob"));
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.Context.ChangeTracker.Clear();
        Use(CurrentRowKind.User, typeof(Member), static (Guid key) => (MemberRead row) => row.Id == key, static (MemberRead row) => row.Id);
        return database;
    }
}
