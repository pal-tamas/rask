using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

/// <summary>
///     The app's own context, from its host's factory and created before <c>Db.Transaction</c>, used with plain EF Core
///     inside it — a LINQ read, a change, <c>SaveChangesAsync</c> — joins the transaction at its first command, with no
///     <c>db:</c> anywhere.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class DbTransactionOwnContextTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-own-context-{Guid.NewGuid():N}.db");
    private ServiceProvider _app = null!;

    public async ValueTask InitializeAsync()
    {
        // A short busy timeout, so a save left on its own connection fails on the transaction's lock in 2 s, not 30.
        Action<DbContextOptionsBuilder> provider = o => o.UseSqlite($"Data Source={_dbPath};Default Timeout=2");
        var services = new ServiceCollection();
        services.AddRaskData<RaskDbContext>();
        services.AddDbContextFactory<RaskDbContext>((sp, o) =>
        {
            provider(o);
            o.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
        });
        services.AddDbContextFactory<RaskReadDbContext>(provider);
        _app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(_app);

        await using var schema = await Contexts.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await schema.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        schema.Add(Widget.Create("anvil"));
        await schema.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Db.Reset();
        await _app.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private IDbContextFactory<RaskDbContext> Contexts => _app.GetRequiredService<IDbContextFactory<RaskDbContext>>();

    [Fact]
    public async Task A_read_a_read_face_and_a_save_on_a_context_made_before_the_block_commit_together()
    {
        await using var db = await Contexts.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var counted = -1;

        await Db.Transaction(async () =>
        {
            var anvil = await db.Set<Widget>().SingleAsync(w => w.Name == "anvil", TestContext.Current.CancellationToken);
            anvil.Rename("renamed");
            counted = await Consignment.Count(TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }, IsolationLevel.Serializable, TestContext.Current.CancellationToken);

        Assert.Equal(0, counted);
        Assert.Equal(["renamed"], await Names());
    }

    [Fact]
    public async Task A_throw_after_the_save_takes_the_update_back()
    {
        await using var db = await Contexts.CreateDbContextAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            var anvil = await db.Set<Widget>().SingleAsync(w => w.Name == "anvil", TestContext.Current.CancellationToken);
            anvil.Rename("renamed");
            await Consignment.Count(TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            throw new InvalidOperationException("roll back");
        }, IsolationLevel.Serializable, TestContext.Current.CancellationToken));

        Assert.Equal(["anvil"], await Names());
    }

    [Fact]
    public async Task A_save_that_is_the_contexts_first_command_joins_too()
    {
        await using var db = await Contexts.CreateDbContextAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Widget.Create("tongs"), cancellationToken: TestContext.Current.CancellationToken);
            db.AddRange(Widget.Create("hammer"), Widget.Create("chisel"));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            throw new InvalidOperationException("roll back");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(["anvil"], await Names());
    }

    [Fact]
    public async Task The_context_saves_on_its_own_connection_after_the_block()
    {
        await using var db = await Contexts.CreateDbContextAsync(TestContext.Current.CancellationToken);

        await Db.Transaction(async () =>
        {
            (await db.Set<Widget>().SingleAsync(w => w.Name == "anvil", TestContext.Current.CancellationToken)).Rename("inside");
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);
        db.Add(Widget.Create("outside"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(["inside", "outside"], await Names());
    }

    private async Task<List<string>> Names()
    {
        await using var read = await Contexts.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var names = await read.Set<Widget>().Select(w => w.Name).ToListAsync(TestContext.Current.CancellationToken);
        return [.. names.Order(StringComparer.Ordinal)];
    }
}
