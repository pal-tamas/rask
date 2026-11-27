using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// A tenant keyed by a typed id over a number, and a table that keeps its tenant as that id. The tenant does not say
// ITenant (see NumberShop); the registration below is the half of the generated CurrentRows.UseTenant that the model
// needs, made as the assembly loads because every test here shares the one model.
public readonly partial record struct BoothShopId(int Value);

public sealed class BoothShop : Aggregate<BoothShopId>
{
    private BoothShop() { }

    public string Name { get; private set; } = "";

    public static BoothShop Open(int id, string name) => new() { Id = new BoothShopId(id), Name = name };
}

public sealed class Booth : Aggregate<int>, IHasForm, IBelongsTo<BoothShop>
{
    private Booth() { }

    public BoothShopId? TenantId { get; private set; }

    public string Name { get; private set; } = "";

    public static Booth Open(int id, string name) => new() { Id = id, Name = name };
}

internal static class TypedTenantRegistration
{
#pragma warning disable CA2255 // the registration a generated module initializer makes in an app
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register() =>
        TenantColumn.Know(new TypedTenantColumn<BoothShopId, int>(static value => new BoothShopId(value), static id => id.Value));
}

/// <summary>
/// A tenant keyed by a typed id: its tables keep the id, are filtered by and stamped with the tenant in flight, and the
/// id travels as the number it holds.
/// </summary>
public sealed class TypedTenantIdTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    public void Dispose()
    {
        CurrentRows.Reset();
        _sqlite.Dispose();
    }

    [Fact]
    public async Task A_table_keeping_the_typed_id_is_stamped_and_filtered_by_the_tenant_in_flight()
    {
        await using var database = await StartAsync();
        await SaveIn(database, 7, Booth.Open(1, "Fish"));
        await SaveIn(database, 8, Booth.Open(2, "Bread"));

        List<Booth> seven;
        List<string> every;
        using (Tenant.Use(7))
        {
            seven = await database.Context.Set<Booth>().ToListAsync(TestContext.Current.CancellationToken);
        }

        using (Tenant.Across())
        {
            every = await database.Context.Set<Booth>().OrderBy(s => s.Id).Select(s => s.Name).ToListAsync(TestContext.Current.CancellationToken);
        }

        var only = Assert.Single(seven);
        Assert.Equal(("Fish", new BoothShopId(7)), (only.Name, only.TenantId));
        Assert.Equal(["Fish", "Bread"], every);
    }

    [Fact]
    public async Task The_read_face_of_a_table_keeping_the_typed_id_is_filtered_too()
    {
        await using var database = await StartAsync();
        await SaveIn(database, 7, Booth.Open(1, "Fish"));
        await SaveIn(database, 8, Booth.Open(2, "Bread"));

        List<string> names;
        using (Tenant.Use(8))
        {
            names = await Booth.Select(s => s.Name);
        }

        Assert.Equal(["Bread"], names);
    }

    [Fact]
    public async Task The_typed_tenant_reads_its_own_row_and_its_id_back_as_the_typed_id()
    {
        await using var database = await StartAsync();
        database.Context.AddRange(BoothShop.Open(7, "Acme"), BoothShop.Open(8, "Globex"));
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.Context.ChangeTracker.Clear();
        CurrentRows.Use(new CurrentRow<BoothShopRead, BoothShopId>(
            CurrentRowKind.Tenant, typeof(BoothShop), static key => row => row.Id == key, static row => row.Id,
            new TypedCurrentRowKey<BoothShopId, int>(static value => new BoothShopId(value))));

        BoothShopRead row;
        int? number;
        using (Tenant.Use(8))
        {
            row = CurrentRows.RequiredTenant<BoothShopRead>();
            number = CurrentRows.TenantId<int>();
        }

        Assert.Equal((new BoothShopId(8), "Globex"), (row.Id, row.Name));
        Assert.Equal(8, number);
    }

    [Fact]
    public void The_typed_id_travels_as_the_number_it_holds_and_back()
    {
        var carried = TenantNumber.ToGuid(7);

        var stamped = TenantColumn.ValueFor(carried, typeof(BoothShopId?), nameof(Booth));
        var back = TenantColumn.TypedCarried(new BoothShopId(7));
        var followed = TenantColumn.ValueColumnOf(typeof(BoothShopId?));

        Assert.Equal(new BoothShopId(7), stamped);
        Assert.Equal(carried, back);
        Assert.Equal(typeof(int?), followed);
    }

    [Fact]
    public void A_tenant_that_carries_no_number_is_refused_by_a_typed_id_over_one()
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => TenantColumn.ValueFor(Guid.CreateVersion7(), typeof(BoothShopId?), nameof(Booth)));

        Assert.Contains("'Booth' keeps its tenant as an int", refused.Message, StringComparison.Ordinal);
    }

    private static async Task SaveIn(TestDatabase database, int tenant, Booth stall)
    {
        using (Tenant.Use(tenant))
        {
            database.Context.Add(stall);
            await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        database.Context.ChangeTracker.Clear();
    }

    private Task<TestDatabase> StartAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString), cancellationToken: TestContext.Current.CancellationToken);
}
