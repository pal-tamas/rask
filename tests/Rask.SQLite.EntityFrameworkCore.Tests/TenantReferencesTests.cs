using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Rask.Cqrs;
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
public sealed class Lorry : Aggregate<string>, IBelongsTo<Firm>
{
    private Lorry() { }

    public Guid? TenantId { get; private set; }

    public static Lorry Of(Guid firm) => new() { Id = Guid.NewGuid().ToString(), TenantId = firm };

    public static void Configure(EntityTypeBuilder<Lorry> builder) =>
        builder.Property(lorry => lorry.TenantId).HasConversion<string>();
}

/// <summary>A tenant keyed by the text of a Guid, whose generated <c>Delete</c> says why it is refused.</summary>
public sealed class Guild : Aggregate<string>, IHasForm, IDeletable
{
    public const string InUse = "The guild still has wagons and cannot be deleted.";

    private Guild() { }

    public string Name { get; private set; } = "";

    public static Guild Named(Guid id, string name) => new() { Id = id.ToString(), Name = name };

    public static void Configure(EntityTypeBuilder<Guild> builder) => builder.WhenReferenced(InUse);
}

/// <summary>A guild's row, its tenant a Guid kept as text against the guild's text key.</summary>
public sealed class Wagon : Aggregate<string>, IBelongsTo<Guild>
{
    private Wagon() { }

    public Guid? TenantId { get; private set; }

    public string Name { get; private set; } = "";

    public static Wagon Named(string name) => new() { Id = Guid.NewGuid().ToString(), Name = name };

    public static void Configure(EntityTypeBuilder<Wagon> builder) =>
        builder.Property(wagon => wagon.TenantId).HasConversion<string>();
}

/// <summary>The app's context: every table that belongs to the guild refers to it once the guild is the app's tenant.</summary>
public sealed class GuildsContext(DbContextOptions<GuildsContext> options) : RaskDbContext(options);

public abstract class FirmsContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Firm>(Firm.Configure);
        modelBuilder.Entity<Lorry>(Lorry.Configure);
    }
}

/// <summary>Rask's conventions — the tenant references among them — before the app's naming rule.</summary>
public sealed class ReferencesFirstContext(DbContextOptions<ReferencesFirstContext> options) : FirmsContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder).ConstraintNames(TenantReferencesTests.Prisma);
}

/// <summary>The app's naming rule declared before Rask's conventions.</summary>
public sealed class NamesFirstContext(DbContextOptions<NamesFirstContext> options) : FirmsContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder.ConstraintNames(TenantReferencesTests.Prisma));
}

/// <summary>The references with no naming rule.</summary>
public sealed class UnnamedReferencesContext(DbContextOptions<UnnamedReferencesContext> options) : FirmsContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);
}

/// <summary>Rask's conventions, and no tenant of the app's: the firm is just a table, and nothing refers to it.</summary>
public sealed class NoReferencesContext(DbContextOptions<NoReferencesContext> options) : FirmsContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);
}

public sealed class TenantReferencesTests : IAsyncLifetime
{
    private readonly SqliteConnection _keeper = new($"Data Source=rask-tenant-refs-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static string? Prisma(Constraint n) => n.Kind == ConstraintKind.ForeignKey ? $"{n.Table}_{n.Columns[0]}_fkey" : null;

    public async ValueTask InitializeAsync() => await _keeper.OpenAsync(Ct);

    public async ValueTask DisposeAsync()
    {
        CurrentRows.Reset();
        await _keeper.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Deleting_a_tenant_that_still_has_rows_is_refused_by_a_database_made_from_the_model()
    {
        TheTenantIsFirm();
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
    public async Task The_generated_delete_of_a_tenant_that_still_has_rows_is_refused_with_its_sentence_and_the_tenant_stays()
    {
        TheTenantIsGuild();
        await using var database = await TestDatabase.Start<GuildsContext>(o => o.UseRaskSqliteAt(_keeper.ConnectionString), cancellationToken: Ct);
        var guild = Guid.NewGuid();
        await Guild.Named(guild, "Coopers").Save(cancellationToken: Ct);
        using (Tenant.Use(guild))
        {
            await Wagon.Named("Barrel cart").Save(cancellationToken: Ct);
        }

        var refused = await Record.ExceptionAsync(() => Guild.Delete(guild.ToString(), cancellationToken: Ct));

        var failure = Assert.Single(Assert.IsType<RaskValidationException>(refused).Failures);
        Assert.Equal(Guild.InUse, failure.Message);
        Assert.Empty(failure.Fields);
        Assert.Equal(["Coopers"], await Guild.Where(g => g.Id == guild.ToString()).Select(g => g.Name));
    }

    [Fact]
    public async Task A_row_of_a_tenant_that_is_not_there_is_refused()
    {
        TheTenantIsFirm();
        await using var db = Open<ReferencesFirstContext>(o => new ReferencesFirstContext(o));
        await db.Database.EnsureCreatedAsync(Ct);

        db.Add(Lorry.Of(Guid.NewGuid()));
        var refused = await Record.ExceptionAsync(() => db.SaveChangesAsync(Ct));

        Assert.IsType<DbUpdateException>(refused);
    }

    [Fact]
    public void The_reference_is_named_by_the_app_s_rule_whichever_is_declared_first()
    {
        TheTenantIsFirm();
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
        TheTenantIsFirm();
        using var db = Open<UnnamedReferencesContext>(o => new UnnamedReferencesContext(o));

        var key = Created(db);

        Assert.Equal("FK_Lorry_Firm_TenantId", key.Name);
    }

    [Fact]
    public void The_migration_snapshot_carries_the_reference_and_nothing_of_the_declaration()
    {
        TheTenantIsFirm();
        using var db = Open<ReferencesFirstContext>(o => new ReferencesFirstContext(o));

        var model = db.GetService<IDesignTimeModel>().Model;

        Assert.Equal("Lorry_TenantId_fkey -> Firm(Id)", model.FindEntityType(typeof(Lorry))!.FindAnnotation("Rask:TenantReference")?.Value);
        Assert.Null(model.FindEntityType(typeof(Firm))!.FindAnnotation("Rask:TenantReference"));
    }

    [Fact]
    public void A_model_whose_app_names_no_tenant_has_no_reference()
    {
        using var db = Open<NoReferencesContext>(o => new NoReferencesContext(o));

        var create = Operations(db).OfType<CreateTableOperation>().Single(c => c.Name == "Lorry");

        Assert.Empty(create.ForeignKeys);
    }

    // The app's tenant, as the generated registration of the aggregate that says ITenant names it, for this test alone.
    private static void TheTenantIsFirm() =>
        CurrentRows.Use(new CurrentRow<FirmRead, string>(
            CurrentRowKind.Tenant, typeof(Firm), static key => row => row.Id == key, static row => row.Id));

    private static void TheTenantIsGuild() =>
        CurrentRows.Use(new CurrentRow<GuildRead, string>(
            CurrentRowKind.Tenant, typeof(Guild), static key => row => row.Id == key, static row => row.Id));

    private static AddForeignKeyOperation Created(DbContext db) =>
        Assert.Single(Operations(db).OfType<CreateTableOperation>().Single(c => c.Name == "Lorry").ForeignKeys);

    private static IReadOnlyList<MigrationOperation> Operations(DbContext db) =>
        db.GetService<IMigrationsModelDiffer>().GetDifferences(null, db.GetService<IDesignTimeModel>().Model.GetRelationalModel());

    private TContext Open<TContext>(Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext =>
        create(new DbContextOptionsBuilder<TContext>().UseRaskSqliteAt(_keeper.ConnectionString).Options);
}
