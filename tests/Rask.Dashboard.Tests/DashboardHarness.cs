using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Cache;
using Rask.Core.Routing;
using Rask.Jobs;
using Rask.Mail;
using Rask.Outbox;

namespace Rask.Dashboard.Tests;

/// <summary>Which batteries a harness should wire up.</summary>
[Flags]
public enum Batteries
{
    None = 0,
    Jobs = 1,
    Outbox = 2,
    Mail = 4,
    Cache = 8,
    All = Jobs | Outbox | Mail | Cache,
}

/// <summary>
/// A context that maps only the batteries it was told to. The point of most of these tests is what the
/// dashboard does when a table ISN'T there, so the mapping has to be selectable.
/// </summary>
public sealed class HarnessDbContext(DbContextOptions<HarnessDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Set by the harness before the provider is built, read by every factory-created context. Static
    /// because EF's DbContextFactory needs exactly one options-only constructor — which is also why this
    /// assembly disables test parallelization (see AssemblyInfo.cs).
    /// </summary>
    public static Batteries Mapped { get; set; } = Batteries.All;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (Mapped.HasFlag(Batteries.Jobs))
        {
            modelBuilder.AddRaskJobs();
        }

        if (Mapped.HasFlag(Batteries.Outbox))
        {
            modelBuilder.AddRaskOutbox();
        }

        if (Mapped.HasFlag(Batteries.Mail))
        {
            modelBuilder.AddRaskMail();
        }

        if (Mapped.HasFlag(Batteries.Cache))
        {
            modelBuilder.AddRaskCache();
        }
    }
}

/// <summary>A real-SQLite provider wired for the dashboard, with a controllable clock.</summary>
public sealed class DashboardHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    public DashboardHarness(
        Batteries registered = Batteries.All,
        Batteries? mapped = null,
        string? environment = null,
        Action<RaskDashboardOptions>? configure = null,
        Action<IServiceCollection>? extra = null)
    {
        DbPath = Path.Combine(Path.GetTempPath(), $"rask-dash-test-{Guid.NewGuid():N}.db");
        HarnessDbContext.Mapped = mapped ?? registered;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment
        {
            EnvironmentName = environment ?? Environments.Production,
        });
        services.AddRaskCqrs();

        // AddRask registers these for a real host; the harness builds its own container, so the pages'
        // routing dependencies have to be named here. LogsPage takes a Navigator because its category
        // filter is a <select> that navigates on change rather than a list of links.
        services.AddScoped<RouteState>();
        services.AddScoped<Navigator>();

        if (registered.HasFlag(Batteries.Jobs))
        {
            services.AddRaskJobs<HarnessDbContext>();
        }

        if (registered.HasFlag(Batteries.Outbox))
        {
            services.AddRaskOutbox<HarnessDbContext>();
        }

        if (registered.HasFlag(Batteries.Mail))
        {
            services.AddRaskMail<HarnessDbContext>(o => o.From = "test@example.com");
        }

        if (registered.HasFlag(Batteries.Cache))
        {
            services.AddRaskCache<HarnessDbContext>();
        }

        extra?.Invoke(services);
        services.AddRaskDashboard<HarnessDbContext>(configure);
        services.AddDbContextFactory<HarnessDbContext>(o => o
            .UseSqlite($"Data Source={DbPath}")
            // EF caches the compiled model per context type, so without folding the mapping into the
            // cache key the FIRST shape built would be reused for every later harness in the process —
            // and the "table isn't mapped" tests would silently see a fully-mapped model.
            .ReplaceService<IModelCacheKeyFactory, HarnessModelCacheKeyFactory>());

        _provider = services.BuildServiceProvider();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public string DbPath { get; }

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    public IServiceProvider Services => _provider;

    public HarnessDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<HarnessDbContext>>().CreateDbContext();

    public IQueuePanel Queue(string slug) =>
        _provider.GetServices<IQueuePanel>().Single(q => q.Slug == slug);

    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        if (File.Exists(DbPath))
        {
            File.Delete(DbPath);
        }
    }
}

/// <summary>
/// Makes <see cref="HarnessDbContext.Mapped"/> part of EF's model cache key, so each mapping shape gets
/// its own compiled model instead of reusing whichever one was built first.
/// </summary>
internal sealed class HarnessModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), HarnessDbContext.Mapped, designTime);
}

/// <summary>A hand-rolled controllable clock — no external package, matching the other suites.</summary>
public sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}

/// <summary>Minimal <see cref="IHostEnvironment"/> so the environment-dependent policy can be tested.</summary>
internal sealed class HostingEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;
    public string ApplicationName { get; set; } = "Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}
