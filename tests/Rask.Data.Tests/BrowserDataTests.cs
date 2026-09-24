using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Cqrs;
using Rask.Query;

namespace Rask.Data.Tests;

/// <summary>
///     In a WebAssembly app, <c>AddRaskData</c> wires what a Rask server host would: the model surface points at the
///     app's context, and a save inside the page's work refetches the page's queries about what it wrote.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class BrowserDataTests : IDisposable
{
    private static readonly QueryOptions Keep = new() { StaleTime = TimeSpan.FromHours(1) };

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-browser-{Guid.NewGuid():N}.db");

    public BrowserDataTests() => Db.Reset();

    public void Dispose()
    {
        Db.Reset();
        File.Delete(_dbPath);
    }

    [Fact]
    public void The_first_entry_into_the_pages_work_points_the_model_surface_at_the_apps_context()
    {
        var page = BrowserApp();

        page.GetRequiredService<ISessionWorkScope>().Enter(page)?.Dispose();

        Assert.True(Db.IsConfigured);
        Assert.True(ReadDb.IsConfigured);
    }

    [Fact]
    public async Task A_save_inside_the_pages_work_refetches_its_queries_about_that_aggregate_and_no_other()
    {
        var page = BrowserApp();
        await CreateSchemaAsync(page);
        var cards = 0;
        var others = 0;
        var queries = page.GetRequiredService<IQueryClient>();
        using var rateCards = queries.Query(QueryKey.For<RateCard>(), _ => Task.FromResult(++cards), Keep);
        using var unrelated = queries.Query(QueryKey.Of("unrelated"), _ => Task.FromResult(++others), Keep);
        await Settled(rateCards);
        await Settled(unrelated);

        using (page.GetRequiredService<ISessionWorkScope>().Enter(page))
        {
            await using var db = Db.CreateContext();
            db.Add(RateCard.For("HU"));
            await db.SaveChangesAsync();
        }

        await Settled(rateCards);
        await Settled(unrelated);
        Assert.Equal(2, rateCards.Data);
        Assert.Equal(1, unrelated.Data);
    }

    [Fact]
    public void The_server_build_of_AddRaskData_leaves_the_session_wiring_to_the_host()
    {
        var services = new ServiceCollection();

        services.AddRaskData<RaskDbContext>();

        // The Rask package's server half registers these; a second IDataChanges would refetch every query twice.
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ISessionWorkScope));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IDataChanges));
    }

    // The container a WASM app builds: one provider for the page, its scoped services resolved from the root.
    private IServiceProvider BrowserApp()
    {
        var services = new ServiceCollection();
        services.AddRaskData<RaskDbContext>();
        services.AddDbContextFactory<RaskDbContext>((sp, o) => o
            .UseSqlite($"Data Source={_dbPath};Pooling=False")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite($"Data Source={_dbPath};Pooling=False"));
        services.AddRaskCqrs();
        services.AddRaskQuery();

        // What AddRaskData adds on its browser build.
        BrowserData.Add(services);
        return services.BuildServiceProvider();
    }

    private static async Task CreateSchemaAsync(IServiceProvider services)
    {
        await using var db = await services.GetRequiredService<IDbContextFactory<RaskDbContext>>().CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    private static async Task Settled<T>(Query<T> query)
    {
        _ = query.Data;
        for (var i = 0; i < 50 && query.IsFetching; i++)
        {
            await Task.Delay(10);
        }
    }
}
