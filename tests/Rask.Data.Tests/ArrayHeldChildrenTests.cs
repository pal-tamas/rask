using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data.Tests.Arrays;

namespace Rask.Data.Tests;

/// <summary>
/// Children an aggregate declares as a read-only collection initialised to <c>[]</c>, <c>Array.Empty</c> or an
/// array — a fixed-size collection EF Core cannot add to — load, refill, add and remove like any other.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class ArrayHeldChildrenTests : IAsyncLifetime
{
    private const int Acme = 7;

    private static readonly Guid Initech = Guid.CreateVersion7();

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-array-children-{Guid.NewGuid():N}.db");
    private readonly string _handWiredPath = Path.Combine(Path.GetTempPath(), $"rask-array-hand-wired-{Guid.NewGuid():N}.db");
    private ServiceProvider _app = null!;

    public async ValueTask InitializeAsync()
    {
        var connection = $"Data Source={_dbPath};Pooling=False";
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData<RaskDbContext>();
        services.AddDbContextFactory<RaskDbContext>((sp, o) => o
            .UseSqlite(connection)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite(connection));
        services.AddDbContextFactory<HandWiredTankerContext>((sp, o) => o
            .UseSqlite($"Data Source={_handWiredPath};Pooling=False")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        _app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(_app);

        await using var context = await OpenAsync();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Db.Reset();
        await _app.DisposeAsync();
        File.Delete(_dbPath);
        File.Delete(_handWiredPath);
    }

    [Fact]
    public async Task A_tanker_with_rows_declared_empty_loads_them_and_their_readings_by_id()
    {
        var issued = await IssueAsync((1, 12.5m, [100, 101]), (2, 3.25m, []));

        Tanker? found;
        using (Tenant.Use(Acme))
        {
            found = await Tanker.Find(issued.Id, cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.NotNull(found);
        Assert.Equal(["1:12.5:100,101", "2:3.25:"], Describe(found));
    }

    [Fact]
    public async Task A_tanker_found_refilled_and_saved_syncs_its_rows_and_readings()
    {
        var issued = await IssueAsync((1, 12.5m, [100, 101]), (2, 3.25m, [5]), (3, 2m, []));

        using (Tenant.Use(Acme))
        {
            var tanker = await Tanker.Find(issued.Id, cancellationToken: TestContext.Current.CancellationToken);
            tanker!.Fill([(1, 12.5m, [101, 102]), (2, 4m, [5]), (4, 1m, [7])]);
            await tanker.Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Equal(["1:12.5:101,102", "2:4:5", "4:1:7"], await StoredAsync(issued.Id));
    }

    [Fact]
    public async Task A_tanker_refilled_in_place_syncs_its_rows_and_readings()
    {
        var issued = await IssueAsync((1, 12.5m, [100]), (2, 3.25m, []));

        using (Tenant.Use(Acme))
        {
            await Tanker.Update(issued.Id, s => s.Fill([(2, 3.25m, [9]), (3, 1m, [])]),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Equal(["2:3.25:9", "3:1:"], await StoredAsync(issued.Id));
    }

    [Fact]
    public async Task A_query_including_the_rows_loads_them_tracked_and_untracked()
    {
        var issued = await IssueAsync((1, 12.5m, [100]), (2, 3.25m, [200, 201]));
        await using var context = await OpenAsync();

        Tanker tracked;
        Tanker untracked;
        using (Tenant.Use(Acme))
        {
            tracked = await context.Set<Tanker>().Include(s => s.Fuellings).ThenInclude(f => f.Readings)
                .SingleAsync(s => s.Id == issued.Id, TestContext.Current.CancellationToken);
            untracked = await context.Set<Tanker>().AsNoTracking()
                .Include(s => s.Fuellings).ThenInclude(f => f.Readings)
                .SingleAsync(s => s.Id == issued.Id, TestContext.Current.CancellationToken);
        }

        Assert.Equal(["1:12.5:100", "2:3.25:200,201"], Describe(tracked));
        Assert.Equal(["1:12.5:100", "2:3.25:200,201"], Describe(untracked));
    }

    [Fact]
    public async Task A_tote_of_guid_tenants_loads_adds_and_removes_items_keyed_by_the_store()
    {
        var tote = Tote.Packed("C-1", "bolt", "nut");
        using (Tenant.Use(Initech))
        {
            await tote.Save(cancellationToken: TestContext.Current.CancellationToken);

            var found = await Tote.Find(tote.Id, cancellationToken: TestContext.Current.CancellationToken);
            found!.Remove("nut");
            found.Add("washer");
            found.Items.Single(i => i.Name == "bolt").Tag("steel");
            await found.Save(cancellationToken: TestContext.Current.CancellationToken);

            await Tote.Update(tote.Id, c => c.Add("screw"), cancellationToken: TestContext.Current.CancellationToken);
        }

        Tote? stored;
        using (Tenant.Use(Initech))
        {
            stored = await Tote.Find(tote.Id, cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Equal(
            ["bolt:new,steel", "screw:new", "washer:new"],
            stored!.Items.Select(static i => i.Name + ":" + string.Join(',', i.Tags.Select(static t => t.Text).Order(StringComparer.Ordinal)))
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_context_wired_by_hand_loads_rows_declared_empty()
    {
        var factory = _app.GetRequiredService<IDbContextFactory<HandWiredTankerContext>>();
        await using (var schema = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            await schema.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        Tanker loaded;
        using (Tenant.Use(Acme))
        {
            await using (var writer = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
            {
                await Tanker.Issue("H-1", (1, 2m, [10, 11])).Save(db: writer, cancellationToken: TestContext.Current.CancellationToken);
                await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await using var reader = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
            loaded = await reader.Set<Tanker>().Include(t => t.Fuellings).ThenInclude(f => f.Readings)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(["1:2:10,11"], Describe(loaded));
    }

    [Fact]
    public async Task A_squad_created_and_updated_from_its_form_holds_what_the_form_posted()
    {
        var created = await Squad.Create(
            new SquadModel { Name = "A", Skills = ["drive"], Posts = [new() { Shift = "early" }, new() { Shift = "late" }] },
            cancellationToken: TestContext.Current.CancellationToken);

        var model = (await Squad.Find(created.Id, cancellationToken: TestContext.Current.CancellationToken))!.ToModel();
        model.Skills = ["drive", "lift"];
        model.Posts.RemoveAll(static p => p.Shift == "late");
        model.Posts.Add(new() { Shift = "night" });
        await Squad.Update(created.Id, model, cancellationToken: TestContext.Current.CancellationToken);

        var stored = await Squad.Find(created.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["drive", "lift"], stored!.Skills);
        Assert.Equal(["early", "night"], stored.Posts.Select(static p => p.Shift).Order(StringComparer.Ordinal));
    }

    private static async Task<Tanker> IssueAsync(params (int, decimal, int[])[] rows)
    {
        using (Tenant.Use(Acme))
        {
            var tanker = Tanker.Issue("S-1", rows);
            await tanker.Save(cancellationToken: TestContext.Current.CancellationToken);
            return tanker;
        }
    }

    private async Task<List<string>> StoredAsync(int tankerId)
    {
        using (Tenant.Use(Acme))
        {
            var tanker = await Tanker.Find(tankerId, cancellationToken: TestContext.Current.CancellationToken);
            return Describe(tanker!);
        }
    }

    private static List<string> Describe(Tanker tanker) =>
    [
        .. tanker.Fuellings.OrderBy(static f => f.PumpNo).Select(static f =>
            FormattableString.Invariant($"{f.PumpNo}:{f.Quantity:0.###}:") +
            string.Join(',', f.Readings.Select(static r => r.Litres).Order())),
    ];

    private async Task<RaskDbContext> OpenAsync() =>
        await _app.GetRequiredService<IDbContextFactory<RaskDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
}
