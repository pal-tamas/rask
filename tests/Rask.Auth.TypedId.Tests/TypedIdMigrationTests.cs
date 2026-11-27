using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Rask.Data;
using Rask.SqlServer;

namespace Rask.Auth.TypedId.Tests;

/// <summary>
///     The account as an app had it before moving to typed ids: keyed by a Guid, its tenant a <c>Guid?</c> column.
///     Generic, so no generator takes it for this project's user or a second tenant.
/// </summary>
/// <typeparam name="TShape">Unused: what keeps the type out of the generators.</typeparam>
public sealed class GuidUser<TShape> : Authenticatable
{
    /// <summary>The column the old base class kept the tenant in.</summary>
    public Guid? TenantId { get; private set; }
}

/// <summary>The tenant as the app had it: keyed by a Guid.</summary>
/// <typeparam name="TShape">Unused: what keeps the type out of the generators.</typeparam>
public sealed class GuidTenant<TShape> : Aggregate<Guid>
{
    /// <summary>The company's name.</summary>
    public string Name { get; private set; } = "";
}

/// <summary>
///     The model of the app before the move: the same Rask context, its typed tables swapped for the Guid-keyed ones
///     under the same names.
/// </summary>
internal sealed class BeforeTypedIds(DbContextOptions<BeforeTypedIds> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The app's own tables first, so the Rask conventions the base applies reach them as they reach the app's
        // generated ones; then the account tables, after the base, as RaskApp's context maps them.
        modelBuilder.Entity<GuidTenant<BeforeTypedIds>>().ToTable(nameof(Tenant));
        modelBuilder.Entity<GuidUser<BeforeTypedIds>>().ToTable(nameof(User));
        base.OnModelCreating(modelBuilder);
        modelBuilder.Ignore<User>();
        modelBuilder.Ignore<Tenant>();
        modelBuilder.AddRaskAuth<GuidUser<BeforeTypedIds>, Guid>();
    }
}

/// <summary>
///     Moving <c>User : Authenticatable</c> to <c>User : Authenticatable&lt;UserId&gt;, IBelongsTo&lt;Tenant&gt;</c> (and the
///     tenant to a <c>TenantId</c>) changes no column: a typed id over a Guid is stored as that Guid, so the account's key,
///     its sessions' and passkeys' <c>UserId</c> and its <c>TenantId</c> keep their store types.
/// </summary>
public sealed class TypedIdMigrationTests
{
    // What the generated registration of a non-generic account says: Authenticatable's IHasTimestamps, IVersioned,
    // IHasForm and IDeletable.
    static TypedIdMigrationTests() =>
        ConventionRegistry.Declare(
            typeof(GuidUser<BeforeTypedIds>),
            TraitSet.Created | TraitSet.Updated | TraitSet.Versioned | TraitSet.CreateModel | TraitSet.UpdateModel | TraitSet.Deletable);

    [Fact]
    public void Moving_the_account_and_its_tenant_to_typed_ids_over_a_guid_only_adds_the_tenant_s_foreign_key()
    {
        using var before = new BeforeTypedIds(SqlServer<BeforeTypedIds>());
        using var after = new TypedDb(SqlServer<TypedDb>());

        var operations = after.GetService<IMigrationsModelDiffer>().GetDifferences(
            before.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            after.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        var added = Assert.IsType<AddForeignKeyOperation>(Assert.Single(operations));
        Assert.Equal((nameof(User), nameof(Tenant)), (added.Table, added.PrincipalTable));
        Assert.Equal([Columns.TenantId], added.Columns);
    }

    // SQL Server, where a Guid is a uniqueidentifier and text is not: the provider that would show a key that changed type.
    private static DbContextOptions<TContext> SqlServer<TContext>()
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>().UseRaskSqlServer(Configured(), o => o.CompatibilityLevel = 160).Options;

    private static IServiceProvider Configured() =>
        new ConfigurationServices(new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Rask:ConnectionStrings:App", "Server=localhost;Database=Rask")])
            .Build());

    // Just the configuration: all UseRaskSqlServer asks the provider for.
    private sealed class ConfigurationServices(IConfiguration configuration) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IConfiguration) ? configuration : null;
    }
}
