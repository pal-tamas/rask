using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

/// <summary>A tenant whose key is the text of a Guid, as TA's <c>Partner</c> is to its <c>Tenant</c> table.</summary>
public sealed class Firm : Aggregate<string>
{
    public const string InUse = "The firm still has data.";

    private Firm() { }

    public string Name { get; private set; } = "";

    public static Firm Named(Guid id, string name) => new() { Id = id.ToString(), Name = name };

    public static void Configure(EntityTypeBuilder<Firm> builder) => builder.WhenReferenced(InUse);
}

/// <summary>A firm's row, its tenant a Guid kept as text.</summary>
public sealed class Lorry : Aggregate<string, PerTenant>
{
    private Lorry() { }

    public Guid? TenantId { get; private set; }

    public static Lorry Of(Guid firm) => new() { Id = Guid.NewGuid().ToString(), TenantId = firm };

    public static void Configure(EntityTypeBuilder<Lorry> builder) =>
        builder.Property(lorry => lorry.TenantId).HasConversion<string>();
}

public abstract class FirmsContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Firm>(Firm.Configure);
        modelBuilder.Entity<Lorry>(Lorry.Configure);
    }
}

/// <summary>The references declared before the app's naming rule.</summary>
public sealed class ReferencesFirstContext(DbContextOptions<ReferencesFirstContext> options) : FirmsContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.TenantReferences<Firm>().ConstraintNames(TenantReferencesTests.Prisma);
}

/// <summary>The app's naming rule declared before the references.</summary>
public sealed class NamesFirstContext(DbContextOptions<NamesFirstContext> options) : FirmsContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ConstraintNames(TenantReferencesTests.Prisma).TenantReferences<Firm>();
}

/// <summary>The references with no naming rule.</summary>
public sealed class UnnamedReferencesContext(DbContextOptions<UnnamedReferencesContext> options) : FirmsContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.TenantReferences<Firm>();
}

/// <summary>No references at all: the model as it was before.</summary>
public sealed class NoReferencesContext(DbContextOptions<NoReferencesContext> options) : FirmsContext(options);

public sealed class TenantReferencesTests : IAsyncLifetime
{
    private readonly SqliteConnection _keeper = new($"Data Source=rask-tenant-refs-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static string? Prisma(Constraint n) => n.Kind == ConstraintKind.ForeignKey ? $"{n.Table}_{n.Columns[0]}_fkey" : null;

    public async ValueTask InitializeAsync() => await _keeper.OpenAsync(Ct);

    public async ValueTask DisposeAsync()
    {
        await _keeper.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Deleting_a_tenant_that_still_has_rows_is_refused_by_a_database_made_from_the_model()
    {
        await using var db = Open<ReferencesFirstContext>(o => new ReferencesFirstContext(o));
        await db.Database.EnsureCreatedAsync(Ct);
        var firm = Guid.NewGuid();
        db.Add(Firm.Named(firm, "Acme"));
        await db.SaveChangesAsync(Ct);
        db.Add(Lorry.Of(firm));
        await db.SaveChangesAsync(Ct);

        db.Remove(await db.Set<Firm>().SingleAsync(Ct));
        var refused = await Record.ExceptionAsync(() => db.SaveChangesAsync(Ct));

        Assert.Contains("FOREIGN KEY constraint failed", Assert.IsType<DbUpdateException>(refused).InnerException?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_row_of_a_tenant_that_is_not_there_is_refused()
    {
        await using var db = Open<ReferencesFirstContext>(o => new ReferencesFirstContext(o));
        await db.Database.EnsureCreatedAsync(Ct);

        db.Add(Lorry.Of(Guid.NewGuid()));
        var refused = await Record.ExceptionAsync(() => db.SaveChangesAsync(Ct));

        Assert.IsType<DbUpdateException>(refused);
    }

    [Fact]
    public void The_reference_is_named_by_the_app_s_rule_whichever_is_declared_first()
    {
        using var referencesFirst = Open<ReferencesFirstContext>(o => new ReferencesFirstContext(o));
        using var namesFirst = Open<NamesFirstContext>(o => new NamesFirstContext(o));

        var one = Created(referencesFirst);
        var other = Created(namesFirst);

        Assert.Equal("Lorry_TenantId_fkey", one.Name);
        Assert.Equal("Lorry_TenantId_fkey", other.Name);
        Assert.Equal(["TenantId"], one.Columns);
        Assert.Equal("Firm", one.PrincipalTable);
        Assert.Equal(["Id"], one.PrincipalColumns!);
        Assert.Equal(ReferentialAction.NoAction, one.OnDelete);
    }

    [Fact]
    public void Without_a_rule_the_reference_is_named_as_EF_Core_names_a_foreign_key()
    {
        using var db = Open<UnnamedReferencesContext>(o => new UnnamedReferencesContext(o));

        var key = Created(db);

        Assert.Equal("FK_Lorry_Firm_TenantId", key.Name);
    }

    [Fact]
    public void The_migration_snapshot_carries_the_reference_and_nothing_of_the_declaration()
    {
        using var db = Open<ReferencesFirstContext>(o => new ReferencesFirstContext(o));

        var model = db.GetService<IDesignTimeModel>().Model;

        Assert.Equal("Lorry_TenantId_fkey -> Firm(Id)", model.FindEntityType(typeof(Lorry))!.FindAnnotation("Rask:TenantReference")?.Value);
        Assert.Null(model.FindEntityType(typeof(Firm))!.FindAnnotation("Rask:TenantReference"));
        Assert.Null(model.FindAnnotation("Rask:TenantReferencesTo"));
    }

    [Fact]
    public void A_model_without_the_declaration_has_no_reference()
    {
        using var db = Open<NoReferencesContext>(o => new NoReferencesContext(o));

        var create = Operations(db).OfType<CreateTableOperation>().Single(c => c.Name == "Lorry");

        Assert.Empty(create.ForeignKeys);
    }

    private static AddForeignKeyOperation Created(DbContext db) =>
        Assert.Single(Operations(db).OfType<CreateTableOperation>().Single(c => c.Name == "Lorry").ForeignKeys);

    private static IReadOnlyList<MigrationOperation> Operations(DbContext db) =>
        db.GetService<IMigrationsModelDiffer>().GetDifferences(null, db.GetService<IDesignTimeModel>().Model.GetRelationalModel());

    private TContext Open<TContext>(Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext =>
        create(new DbContextOptionsBuilder<TContext>().UseRaskSqliteAt(_keeper.ConnectionString).Options);
}
