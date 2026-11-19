using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

/// <summary>A shelf whose books load themselves when read, through EF Core's injected <see cref="ILazyLoader" />.</summary>
public sealed class LazyShelf
{
    private ICollection<LazyBook>? _books;

    public LazyShelf()
    {
    }

    private LazyShelf(ILazyLoader loader) => Loader = loader;

    public int Id { get; set; }

    public ICollection<LazyBook> Books
    {
        get => Loader.Load(this, ref _books) ?? [];
        set => _books = value;
    }

    private ILazyLoader? Loader { get; }
}

public sealed class LazyBook
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    public int ShelfId { get; set; }
}

public sealed class LazyShelfContext(DbContextOptions<LazyShelfContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<LazyShelf>().HasMany(s => s.Books);
}

/// <summary>
///     Lazy loading is off on a context the Rask interceptors reach: a navigation nobody loaded stays unloaded and runs no
///     query. <c>AllowLazyLoading()</c> is the way back.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class LazyLoadingTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-lazy-{Guid.NewGuid():N}.db");
    private readonly Commands _commands = new();

    [Fact]
    public async Task An_unloaded_navigation_stays_unloaded_and_runs_no_query()
    {
        using var provider = Provider(allow: false);
        await SeedAsync(provider);
        await using var db = provider.GetRequiredService<IDbContextFactory<LazyShelfContext>>().CreateDbContext();
        var shelf = await db.Set<LazyShelf>().SingleAsync(TestContext.Current.CancellationToken);
        var before = _commands.Count;

        var books = shelf.Books;

        Assert.Empty(books);
        Assert.Equal(before, _commands.Count);
        Assert.False(db.ChangeTracker.LazyLoadingEnabled);
    }

    [Fact]
    public async Task A_context_that_allows_lazy_loading_loads_a_navigation_when_it_is_read()
    {
        using var provider = Provider(allow: true);
        await SeedAsync(provider);
        await using var db = provider.GetRequiredService<IDbContextFactory<LazyShelfContext>>().CreateDbContext();
        var shelf = await db.Set<LazyShelf>().SingleAsync(TestContext.Current.CancellationToken);

        var books = shelf.Books;

        Assert.Equal(2, books.Count);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private ServiceProvider Provider(bool allow)
    {
        var services = new ServiceCollection();
        services.AddRaskData();
        services.AddDbContextFactory<LazyShelfContext>((sp, o) =>
        {
            o.UseSqlite($"Data Source={_dbPath};Pooling=False")
                .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>())
                .AddInterceptors(_commands);
            if (allow)
            {
                o.AllowLazyLoading();
            }
        });
        return services.BuildServiceProvider();
    }

    private static async Task SeedAsync(ServiceProvider provider)
    {
        await using var db = provider.GetRequiredService<IDbContextFactory<LazyShelfContext>>().CreateDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        db.Add(new LazyShelf { Books = [new LazyBook { Title = "a" }, new LazyBook { Title = "b" }] });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed class Commands : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecuting(command, eventData, result);
        }
    }
}
