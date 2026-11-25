using Microsoft.EntityFrameworkCore;
using Rask.Data;
using Rask.Tests.Shared;

namespace Rask.WebPush.Tests;

/// <summary>An app's context, mapping the push subscriber table as a RaskApp's does.</summary>
public sealed class BellContext(DbContextOptions<BellContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddRaskWebPush();
    }
}

/// <summary>A <c>TestDatabase</c> over a model that maps the push table stands Web Push in with its fake.</summary>
public sealed class PushTestDatabaseTests : IDisposable
{
    private readonly InMemorySqlite _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task A_push_with_no_host_is_recorded_by_the_database_s_push_fake()
    {
        await using var database = await TestDatabase.Start<BellContext>(o => o.UseSqlite(_db.ConnectionString), cancellationToken: Ct);
        var ada = Guid.NewGuid();

        await Push.Send(WebPushMessage.Text("Order shipped", "#1042 is on its way"), Ct).To(ada);

        database.Push.Sent().To(ada).WithTitle("Order shipped").Once();
    }
}
