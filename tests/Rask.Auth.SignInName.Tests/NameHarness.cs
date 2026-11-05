using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Data;

namespace Rask.Auth.SignInName.Tests;

/// <summary>
///     The application context, built as <c>RaskApp</c>'s is: the app's own model — <see cref="User" /> with its
///     <see cref="Username" /> column — and then the account tables.
/// </summary>
internal sealed class NameDb(DbContextOptions<NameDb> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddRaskAuth();
    }
}

/// <summary>A real-SQLite app wired the way the auth battery wires one, around <see cref="User" />.</summary>
internal sealed class NameHarness : IAsyncDisposable
{
    /// <summary>The token the first registration presents.</summary>
    public const string FirstRunToken = "test-first-run-token";

    private readonly ServiceProvider _provider;
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rask-auth-names-{Guid.NewGuid():N}.db");

    /// <param name="configure">Deviations from the default options.</param>
    public NameHarness(Action<AuthOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskData();
        services.AddDbContextFactory<NameDb>((sp, o) => o
            .UseSqlite($"Data Source={_path};Pooling=False")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        // A real hash at a fraction of the work, registered first so AddRaskAuth's TryAdd keeps it.
        services.AddSingleton(new PasswordHasher(iterations: 1_000));
        services.AddRaskAuth<NameDb>(o =>
        {
            o.FirstRunToken = FirstRunToken;
            configure?.Invoke(o);
        });

        _provider = services.BuildServiceProvider();
    }

    public IServiceProvider Services => _provider;

    public IAccounts Accounts => _provider.GetRequiredService<IAccounts>();

    public IAuthSessions Sessions => _provider.GetRequiredService<IAuthSessions>();

    /// <summary>Creates the schema and runs the start-up work: the model check and the first-run token.</summary>
    public async Task StartAsync()
    {
        await using (var db = NewContext())
        {
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        foreach (var hosted in _provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(TestContext.Current.CancellationToken);
        }
    }

    public NameDb NewContext() => _provider.GetRequiredService<IDbContextFactory<NameDb>>().CreateDbContext();

    /// <summary>The user named <paramref name="name" />, as it is stored now.</summary>
    public async Task<User> UserAsync(string name)
    {
        await using var db = NewContext();
        var username = new Username(name);
        return await db.Set<User>().AsNoTracking().SingleAsync(u => u.Username == username, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();

        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test over.
        }
    }
}
