using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Data;
using Rask.Tests.Shared;

namespace Rask.Auth.TypedId.Tests;

/// <summary>
///     The application context, built as <c>RaskApp</c>'s is: the app's own model — <see cref="Tenant" /> and
///     <see cref="User" />, both keyed by typed ids — and then the account tables.
/// </summary>
internal sealed class TypedDb(DbContextOptions<TypedDb> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddRaskAuth();
    }
}

/// <summary>A real-SQLite app, its database in memory and its own, wired the way the auth battery wires one, around <see cref="User" />.</summary>
internal sealed class TypedHarness : IAsyncDisposable
{
    /// <summary>The token the first registration presents.</summary>
    public const string FirstRunToken = "test-first-run-token";

    private readonly ServiceProvider _provider;
    private readonly InMemorySqlite _database = new();

    /// <param name="configure">Deviations from the default options.</param>
    /// <param name="extraServices">Registrations a test adds last, such as the context a <c>Db.Transaction</c> opens.</param>
    public TypedHarness(Action<AuthOptions>? configure = null, Action<IServiceCollection>? extraServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskData();
        services.AddDbContextFactory<TypedDb>((sp, o) => o
            .UseSqlite(_database.ConnectionString)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        // A real hash at a fraction of the work, registered first so AddRaskAuth's TryAdd keeps it.
        services.AddSingleton(new PasswordHasher(iterations: 1_000));
        services.AddRaskAuth<TypedDb>(o =>
        {
            o.FirstRunToken = FirstRunToken;
            configure?.Invoke(o);
        });

        extraServices?.Invoke(services);

        _provider = services.BuildServiceProvider();
    }

    public IServiceProvider Services => _provider;

    public IAccountFlows Accounts => _provider.GetRequiredService<IAccountFlows>();

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

    /// <summary>
    ///     Points the read faces at this database for the calling flow — what <c>Current.Tenant</c> and a tenant's
    ///     existence are read through. Not async, so call it in the test itself.
    /// </summary>
    /// <returns>The binding.</returns>
    public IDisposable BindReadFaces()
    {
        var options = new DbContextOptionsBuilder<RaskReadDbContext>().UseSqlite(_database.ConnectionString).Options;
        return ReadDb.Use(() => new RaskReadDbContext(options));
    }

    public TypedDb NewContext() => _provider.GetRequiredService<IDbContextFactory<TypedDb>>().CreateDbContext();

    /// <summary>The user signing in with <paramref name="email" />, as it is stored now.</summary>
    public async Task<User> UserAsync(string email)
    {
        await using var db = NewContext();
        return await db.Set<User>().AsNoTracking().SingleAsync(u => u.Email == email, TestContext.Current.CancellationToken);
    }

    /// <summary>Saves a company named <paramref name="name" />, written straight to its table.</summary>
    /// <returns>Its id.</returns>
    public async Task<TenantId> AddTenantAsync(string name)
    {
        await using var db = NewContext();
        var id = new TenantId(Guid.CreateVersion7());
        var entry = db.Entry(Tenant.Blank());
        entry.Property(nameof(Tenant.Id)).CurrentValue = id;
        entry.Property(nameof(Tenant.Name)).CurrentValue = name;
        entry.State = EntityState.Added;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    /// <summary>Changes the user signing in with <paramref name="email" /> and saves it.</summary>
    public async Task ChangeAsync(string email, Action<User> change)
    {
        await using var db = NewContext();
        var user = await db.Set<User>().SingleAsync(u => u.Email == email, TestContext.Current.CancellationToken);
        change(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _database.DisposeAsync();
    }
}
