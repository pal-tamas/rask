using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.Server.Tests.App;

/// <summary>
///     A hand-wired host's own context — from the factory <c>AddRaskData&lt;TContext&gt;(o =&gt; …)</c> registers, made
///     before <c>Db.Transaction</c> — read, changed and saved with plain EF Core inside it: it joins the transaction at
///     its first command, beside a read face, at serializable isolation.
/// </summary>
[Collection(RaskAppCollection.Name)]
public sealed class HandWiredDbTransactionTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-hand-wired-tx-{Guid.NewGuid():N}.db");
    private ServiceProvider _app = null!;

    public async ValueTask InitializeAsync()
    {
        // A short busy timeout, so a save left on its own connection fails on the transaction's lock in 2 s, not 30.
        Action<DbContextOptionsBuilder> database = o => o.UseSqlite($"Data Source={_dbPath};Default Timeout=2");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<LegacyQuayContext>(database);
        services.AddRaskData<QuayDomainContext>(database);
        _app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(_app);

        await using var legacy = await _app.GetRequiredService<IDbContextFactory<LegacyQuayContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        await legacy.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        legacy.Add(new LegacyTrailer { Id = 1, Weight = 1200, Plate = "ABC-123" });
        await legacy.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Db.Reset();
        await _app.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task A_page_s_context_made_before_the_block_reads_and_saves_inside_it()
    {
        await using var db = await Contexts().CreateDbContextAsync(TestContext.Current.CancellationToken);
        var counted = -1;

        await Db.Transaction(async () =>
        {
            var trailer = await db.Set<Trailer>().SingleAsync(t => t.Id == 1, TestContext.Current.CancellationToken);
            db.Entry(trailer).Property(t => t.Plate).CurrentValue = "XYZ-789";
            counted = await Trailer.Count(TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }, IsolationLevel.Serializable, TestContext.Current.CancellationToken);

        Assert.Equal(1, counted);
        Assert.Equal("XYZ-789", await Plate());
    }

    [Fact]
    public async Task A_throw_after_the_save_takes_the_update_back()
    {
        await using var db = await Contexts().CreateDbContextAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            var trailer = await db.Set<Trailer>().SingleAsync(t => t.Id == 1, TestContext.Current.CancellationToken);
            db.Entry(trailer).Property(t => t.Plate).CurrentValue = "XYZ-789";
            await Trailer.Count(TestContext.Current.CancellationToken);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            throw new InvalidOperationException("roll back");
        }, IsolationLevel.Serializable, TestContext.Current.CancellationToken));

        Assert.Equal("ABC-123", await Plate());
    }

    private IDbContextFactory<QuayDomainContext> Contexts() =>
        _app.GetRequiredService<IDbContextFactory<QuayDomainContext>>();

    private async Task<string> Plate()
    {
        await using var legacy = await _app.GetRequiredService<IDbContextFactory<LegacyQuayContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        return (await legacy.Trailers.SingleAsync(t => t.Id == 1, TestContext.Current.CancellationToken)).Plate;
    }
}
