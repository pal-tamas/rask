using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Auth.Tests;

/// <summary>The application context an app would write, with the auth tables mapped onto it.</summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskAuth();
}

/// <summary>
///     A real-SQLite service provider wired the way the auth battery wires a scaffolded app.
/// </summary>
/// <remarks>
///     Deliberately a file rather than <c>:memory:</c>: the concurrency tests need several connections
///     to see one database, which an in-memory database only does through a shared cache and a kept-open
///     connection. A file is what a real app uses, and it is what makes a contended write actually contend.
/// </remarks>
public sealed class AuthHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly bool _ownsFile;

    /// <param name="configure">Deviations from the default options.</param>
    /// <param name="dbPath">
    ///     An existing database to reopen. Passing the <see cref="DbPath" /> of another harness is how a
    ///     test restarts the same app: the file is the only thing a restart actually keeps.
    /// </param>
    public AuthHarness(Action<AuthOptions>? configure = null, string? dbPath = null)
    {
        _ownsFile = dbPath is null;
        DbPath = dbPath ?? Path.Combine(Path.GetTempPath(), $"rask-auth-test-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<AuthDbContext>(o => o.UseSqlite($"Data Source={DbPath}"));
        services.AddRaskAuth<AuthDbContext>(o =>
        {
            // A fixed token keeps the tests from having to read it back out of the log.
            o.FirstRunToken = FirstRunTokenValue;
            configure?.Invoke(o);
        });

        _provider = services.BuildServiceProvider();

        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    /// <summary>The token the harness configures, so a test can present the right one.</summary>
    public const string FirstRunTokenValue = "test-first-run-token";

    public string DbPath { get; }

    public IServiceProvider Services => _provider;

    public AuthDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<AuthDbContext>>().CreateDbContext();

    /// <summary>Runs the startup step that decides whether a first-run token is pending.</summary>
    public async Task StartAsync()
    {
        foreach (var hosted in _provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }
    }

    /// <summary>A fresh DI scope, as a request or a live session would have.</summary>
    public IServiceScope NewScope() => _provider.CreateScope();

    public FirstRunToken Token => _provider.GetRequiredService<FirstRunToken>();

    /// <summary>The roles held by the account with this email, lowercased.</summary>
    public async Task<IReadOnlyList<string>> RolesOfAsync(string email)
    {
        using var scope = NewScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<RaskUser>>();
        var user = await users.FindByEmailAsync(email);

        return user is null ? [] : (await users.GetRolesAsync(user)).ToArray();
    }

    public async Task<int> UserCountAsync()
    {
        await using var db = NewContext();
        return await db.Set<RaskUser>().CountAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();

        // The pooled connections keep a handle on the file, so the delete below fails without this.
        // Rask has hit this before as an intermittent "database is locked" in unrelated suites.
        SqliteConnection.ClearAllPools();

        if (!_ownsFile)
        {
            // A reopened database belongs to the harness that made it.
            return;
        }

        try
        {
            File.Delete(DbPath);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test over.
        }
    }
}
