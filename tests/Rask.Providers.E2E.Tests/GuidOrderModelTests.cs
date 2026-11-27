using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Rask.Providers.E2E.Tests;

/// <summary>The convoys as a database made before Rask stored ids by time holds them: the convention taken out.</summary>
public sealed class NativeConvoyDbContext(DbContextOptions<NativeConvoyDbContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ModelRegistry.ApplyConventions(configurationBuilder);
        configurationBuilder.Conventions.Remove(typeof(SqlServerGuidOrderConvention));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ConvoyDbContext.Schema);
        modelBuilder.Entity<Convoy>();
        modelBuilder.Entity<ConvoyLeg>().HasOne<Convoy>().WithMany().HasForeignKey(l => l.Convoy);
        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>
///     The time-ordered Guid columns as a model and a migration, without a server: a model needs a provider but no
///     connection, and the differ compares two models.
/// </summary>
public sealed class GuidOrderModelTests
{
    private const string Offline = "Server=(local);Database=rask_model_only;Trusted_Connection=True";

    [Fact]
    public void On_UseRaskSqlServer_a_typed_id_its_foreign_key_and_every_plain_Guid_are_stored_by_time()
    {
        using var context = Ordered();
        var convoy = context.Model.FindEntityType(typeof(Convoy))!;
        var leg = context.Model.FindEntityType(typeof(ConvoyLeg))!;
        var id = ConvoyId.Create();

        object? Stored(IEntityType type, string property, object value) =>
            type.FindProperty(property)!.GetTypeMapping().Converter!.ConvertToProvider(value);

        Assert.Equal(SqlServerGuidOrder.ByTime, context.GetService<IDesignTimeModel>().Model.FindAnnotation(SqlServerGuidOrder.Annotation)?.Value);
        Assert.Equal(SqlServerGuidOrder.ToStore(id.Value), Stored(convoy, nameof(Convoy.Id), id));
        Assert.Equal(SqlServerGuidOrder.ToStore(id.Value), Stored(leg, nameof(ConvoyLeg.Convoy), id));
        Assert.Equal(SqlServerGuidOrder.ToStore(id.Value), Stored(convoy, nameof(Convoy.Ticket), id.Value));
        Assert.Equal(SqlServerGuidOrder.ToStore(id.Value), Stored(convoy, nameof(Convoy.Spare), id.Value));
        Assert.Equal(SqlServerGuidOrder.ToStore(id.Value), Stored(leg, nameof(ConvoyLeg.Id), id.Value));
        Assert.Equal(id, convoy.FindProperty(nameof(Convoy.Id))!.GetTypeMapping().Converter!.ConvertFromProvider(SqlServerGuidOrder.ToStore(id.Value)));
    }

    [Fact]
    public void Storing_by_time_adds_no_default_and_changes_no_key_s_generation()
    {
        using var ordered = Ordered();
        using var native = Native();

        static IEnumerable<string> Generation(DbContext db) =>
            db.Model.GetEntityTypes().SelectMany(static e => e.GetProperties())
                .Select(static p => $"{p.DeclaringType.Name}.{p.Name}:{p.ValueGenerated}:{p.GetDefaultValueSql()}:{p.GetValueGeneratorFactory() is null}");

        Assert.Equal(Generation(native), Generation(ordered));
        Assert.Equal(native.Database.GenerateCreateScript(), ordered.Database.GenerateCreateScript());
    }

    [Fact]
    public void The_migration_that_first_stores_by_time_rewrites_the_rows_once_and_back_again()
    {
        using var ordered = Ordered();
        using var before = Native();
        var differ = ordered.GetService<IMigrationsModelDiffer>();
        var native = before.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var model = ordered.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        var first = differ.GetDifferences(native, model);
        var again = differ.GetDifferences(model, model);
        var back = differ.GetDifferences(model, native);

        var rewrite = Assert.IsType<SqlOperation>(Assert.Single(first)).Sql;
        Assert.StartsWith(GuidOrderRewrite.Marker, rewrite, StringComparison.Ordinal);
        Assert.Contains("NOCHECK CONSTRAINT", rewrite, StringComparison.Ordinal);
        Assert.Contains("WITH CHECK CHECK CONSTRAINT", rewrite, StringComparison.Ordinal);
        Assert.Contains("UPDATE [rask_guid_order].[convoy_legs] SET [id] = " + SqlServerGuidOrder.StoredFromNative("[id]") + ", [convoy] = ", rewrite, StringComparison.Ordinal);
        Assert.Contains("[spare] = " + SqlServerGuidOrder.StoredFromNative("[spare]"), rewrite, StringComparison.Ordinal);
        Assert.Empty(again);
        Assert.Contains("[id] = " + SqlServerGuidOrder.NativeFromStored("[id]"), Assert.IsType<SqlOperation>(Assert.Single(back)).Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void An_app_started_without_that_migration_has_a_pending_model_change()
    {
        using var ordered = Ordered();
        using var before = Native();
        var differ = ordered.GetService<IMigrationsModelDiffer>();
        var native = before.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var model = ordered.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        var pending = differ.HasDifferences(native, model);
        var current = differ.HasDifferences(model, model);

        Assert.True(pending);
        Assert.False(current);
    }

    [Fact]
    public void The_rewrite_is_a_migration_no_older_version_may_run_beside()
    {
        using var ordered = Ordered();
        using var before = Native();
        var operations = ordered.GetService<IMigrationsModelDiffer>().GetDifferences(
            before.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            ordered.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        var exclusive = ExclusiveMigrations.IsExclusive(new Written(operations));
        var ordinary = ExclusiveMigrations.IsExclusive(new Written([new SqlOperation { Sql = "UPDATE x SET y = 1" }]));

        Assert.True(exclusive);
        Assert.False(ordinary);
    }

    [Fact]
    public void Without_Rask_s_differ_SQL_Server_keeps_the_native_form_it_could_not_rewrite()
    {
        using var context = new ConvoyDbContext(new DbContextOptionsBuilder<ConvoyDbContext>().UseSqlServer(Offline).Options);

        var ticket = context.Model.FindEntityType(typeof(Convoy))!.FindProperty(nameof(Convoy.Ticket))!;

        Assert.Null(ticket.GetTypeMapping().Converter);
        Assert.Null(context.GetService<IDesignTimeModel>().Model.FindAnnotation(SqlServerGuidOrder.Annotation));
    }

    [Fact]
    public void On_PostgreSQL_the_ids_are_stored_as_they_were()
    {
        using var context = new ConvoyDbContext(new DbContextOptionsBuilder<ConvoyDbContext>().UseRaskPostgresAt("Host=localhost;Database=rask_model_only").Options);

        var ticket = context.Model.FindEntityType(typeof(Convoy))!.FindProperty(nameof(Convoy.Ticket))!;

        Assert.Null(ticket.GetTypeMapping().Converter);
        Assert.Null(context.GetService<IDesignTimeModel>().Model.FindAnnotation(SqlServerGuidOrder.Annotation));
    }

    private static ConvoyDbContext Ordered() =>
        new(new DbContextOptionsBuilder<ConvoyDbContext>().UseRaskSqlServerAt(Offline).Options);

    private static NativeConvoyDbContext Native() =>
        new(new DbContextOptionsBuilder<NativeConvoyDbContext>().UseRaskSqlServerAt(Offline).Options);

    private sealed class Written : Migration
    {
        private readonly IReadOnlyList<MigrationOperation> _operations;

        public Written(IReadOnlyList<MigrationOperation> operations) => _operations = operations;

        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Operations.AddRange(_operations);
    }
}
