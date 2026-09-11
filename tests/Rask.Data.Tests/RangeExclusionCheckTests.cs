// EF1001: SqliteMigrationsSqlGenerator is EF-internal; subclassing it is how a provider package adds DDL, so the
// stand-in enforcer below is built the same way the real ones are.
#pragma warning disable EF1001

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Sqlite.Migrations.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Data.Tests;

// HasNonOverlappingRange is metadata a provider has to turn into DDL. On a provider that does not, the app used
// to build, migrate and quietly accept the overlapping rows the rule was declared to stop. These pin the boot
// check that refuses that, without opening a connection.
[Collection(DataDbCollection.Name)]
public sealed class RangeExclusionCheckTests
{
    [Fact]
    public async Task A_rule_the_provider_would_ignore_fails_the_boot()
    {
        await using var provider = Build<SlotContext>(o => o.UseSqlite("Data Source=:memory:"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(provider));

        // Names the entity, the provider that ignores it, and the call that enforces it.
        Assert.StartsWith("Slot declares HasNonOverlappingRange, but Microsoft.EntityFrameworkCore.Sqlite does not enforce it", error.Message);
        Assert.Contains("Configure SlotContext with UseRaskSqlite(connectionString)", error.Message);
    }

    [Fact]
    public async Task A_model_without_the_rule_boots_on_any_provider()
    {
        await using var provider = Build<PlainContext>(o => o.UseSqlite("Data Source=:memory:"));

        await StartAsync(provider);
    }

    [Fact]
    public async Task A_provider_whose_migrations_enforce_the_rule_boots()
    {
        await using var provider = Build<SlotContext>(o => o
            .UseSqlite("Data Source=:memory:")
            .ReplaceService<IMigrationsSqlGenerator, EnforcingGenerator>());

        await StartAsync(provider);
    }

    [Fact]
    public async Task A_context_registered_without_a_factory_is_still_checked()
    {
        // AddRaskData<TContext> never required the factory at registration; the check must not start to.
        var services = new ServiceCollection();
        services.AddRaskData<SlotContext>();
        services.AddDbContext<SlotContext>(o => o.UseSqlite("Data Source=:memory:"));
        await using var provider = services.BuildServiceProvider();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(provider));
        Assert.StartsWith("Slot declares HasNonOverlappingRange", error.Message);
    }

    [Fact]
    public async Task No_registered_context_leaves_nothing_to_check()
    {
        var services = new ServiceCollection();
        services.AddRaskData<SlotContext>();
        await using var provider = services.BuildServiceProvider();

        await StartAsync(provider);
    }

    [Fact]
    public void Binding_the_same_context_twice_registers_one_check()
    {
        var services = new ServiceCollection();
        services.AddRaskData<SlotContext>();
        services.AddRaskData<SlotContext>();

        Assert.Single(services, d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(RangeExclusionCheck<SlotContext>));
    }

    private static ServiceProvider Build<TContext>(Action<DbContextOptionsBuilder> configure)
        where TContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddRaskData<TContext>();
        services.AddDbContextFactory<TContext>(configure);
        return services.BuildServiceProvider();
    }

    private static async Task StartAsync(IServiceProvider provider)
    {
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }
    }

    public sealed class Slot
    {
        public int Id { get; set; }

        public long StartsAt { get; set; }

        public long EndsAt { get; set; }
    }

    public sealed class SlotContext(DbContextOptions<SlotContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Slot>().HasNonOverlappingRange(x => x.StartsAt, x => x.EndsAt);
    }

    public sealed class PlainContext(DbContextOptions<PlainContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Slot>();
    }

    private sealed class EnforcingGenerator(
        MigrationsSqlGeneratorDependencies dependencies,
        IRelationalAnnotationProvider migrationsAnnotations)
        : SqliteMigrationsSqlGenerator(dependencies, migrationsAnnotations), IRangeExclusionEnforcer;
}
