using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

// The SkipChangeTracking writer used to open the DbConnection itself. EF's connection interceptors only fire on
// an open EF performs, so UseRaskSqlite's pragmas — and any interceptor an app registered — never ran for the
// rows it wrote. These pin that the writer opens through EF, and that it leaves a caller's open connection open.
[Collection(DataDbCollection.Name)]
public sealed class BulkInsertConnectionTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-bulk-connection-{Guid.NewGuid():N}.db");
    private readonly OpenCounter _opens = new();
    private readonly ServiceProvider _provider;

    public BulkInsertConnectionTests()
    {
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData();
        services.AddDbContextFactory<TestDbContext>((sp, o) => o
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(_opens)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        _provider = services.BuildServiceProvider();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task The_writer_opens_through_EF_so_connection_interceptors_run()
    {
        await using var db = NewContext();
        var before = _opens.Count;

        await db.BulkInsertAsync(Widgets(5), o => o.SkipChangeTracking = true);

        Assert.True(_opens.Count > before, "the fast path opened its connection without EF's interceptors running");
    }

    [Fact]
    public async Task A_connection_the_caller_opened_is_still_open_afterwards()
    {
        await using var db = NewContext();
        await db.Database.OpenConnectionAsync();

        await db.BulkInsertAsync(Widgets(5), o => o.SkipChangeTracking = true);

        Assert.Equal(ConnectionState.Open, db.Database.GetDbConnection().State);
        Assert.Equal(5, await db.Widgets.CountAsync());
    }

    [Fact]
    public async Task A_connection_the_writer_opened_is_closed_afterwards()
    {
        await using var db = NewContext();

        await db.BulkInsertAsync(Widgets(5), o => o.SkipChangeTracking = true);

        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    private TestDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContext();

    // The writer refuses entities carrying domain events, so the fixtures clear them first.
    private static Widget[] Widgets(int count)
    {
        var widgets = Enumerable.Range(0, count).Select(i => Widget.Create($"widget-{i}")).ToArray();
        foreach (var widget in widgets)
        {
            widget.ClearDomainEvents();
        }

        return widgets;
    }

    public void Dispose()
    {
        _provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test over.
        }
    }

    private sealed class OpenCounter : DbConnectionInterceptor
    {
        private int _count;

        public int Count => _count;

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
            Interlocked.Increment(ref _count);

        public override Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return Task.CompletedTask;
        }
    }
}
