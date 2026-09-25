using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.WebPush.Tests;

public sealed class PushDbContext(DbContextOptions<PushDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskWebPush();
}

/// <summary>A sender that answers "delivered" — or "gone", for the endpoints it was told are — and remembers who it reached.</summary>
internal sealed class StubSender : IWebPush
{
    private readonly List<string> _reached = [];

    public HashSet<string> Gone { get; } = [];

    public IReadOnlyList<string> Reached
    {
        get
        {
            lock (_reached)
            {
                return [.. _reached];
            }
        }
    }

    public Task<WebPushResult> Send(PushSubscription subscription, WebPushMessage message, CancellationToken cancellationToken = default)
    {
        // Refuses what the real sender refuses, so a malformed row behaves here as it would in production.
        if (WebPushSender.Problem(subscription) is { } problem)
        {
            throw new ArgumentException(problem, nameof(subscription));
        }

        if (Gone.Contains(subscription.Endpoint))
        {
            return Task.FromResult(new WebPushResult(WebPushStatus.Expired, 410));
        }

        lock (_reached)
        {
            _reached.Add(subscription.Endpoint);
        }

        return Task.FromResult(new WebPushResult(WebPushStatus.Success, 201));
    }
}

/// <summary>The battery on a real SQLite file, with the sender stubbed out unless a test asks for the real one.</summary>
internal sealed class PushHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly string _database = Path.Combine(Path.GetTempPath(), $"rask-push-test-{Guid.NewGuid():N}.db");

    public PushHarness(bool stubSender = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskWebPush<PushDbContext>();
        services.AddDbContextFactory<PushDbContext>(o => o.UseSqlite($"Data Source={_database}"));

        if (stubSender)
        {
            // Last registration wins for a single resolve, so this stands in for the typed-client sender.
            services.AddSingleton<IWebPush>(Sender);
        }

        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<PushDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
    }

    public StubSender Sender { get; } = new();

    public IServiceProvider Services => _provider;

    public IPush Push => _provider.GetRequiredService<IPush>();

    public static PushSubscription Browser(string name) =>
        new($"https://push.example/{name}", "BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8QcYP7DkM", "tBHItJI5svbpez7KI4CCXg");

    public int Rows()
    {
        using var db = _provider.GetRequiredService<IDbContextFactory<PushDbContext>>().CreateDbContext();
        return db.Set<PushSubscriber>().Count();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_database);
    }
}
