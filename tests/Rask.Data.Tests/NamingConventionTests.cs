using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests.Naming;

public sealed record Odometer(int Start, int End);

public sealed record Stop(string Place, int Minutes);

public sealed class Refuel
{
    public string Id { get; set; } = "";

    public string TenantId { get; set; } = "";

    public DateTime CountedAt { get; set; }

    public DateTime Moment { get; set; }

    public string VATNumber { get; set; } = "";

    public Odometer Km { get; set; } = new(0, 0);

    public Odometer Trip { get; set; } = new(0, 0);

    public List<Stop> Stops { get; set; } = [];

    public string? TankerId { get; set; }

    public Tanker? Tanker { get; set; }
}

public sealed class Tanker
{
    public string Id { get; set; } = "";

    public string Plate { get; set; } = "";

    public Depot Depot { get; set; } = new();

    public List<Leg> Legs { get; set; } = [];
}

public sealed class Leg
{
    public int Id { get; set; }

    public int DrivenKm { get; set; }
}

/// <summary>A table a battery maps, recorded as one the way every battery's configuration records its own.</summary>
public sealed class Ledger
{
    public int Id { get; set; }

    public int EntryCount { get; set; }
}

/// <summary>An app's own context deriving from Rask's, so the generated value-object mapping is in its model.</summary>
public sealed class CamelGondolaContext(DbContextOptions<CamelGondolaContext> options) : RaskDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.ColumnNames(n => n.CamelCase);
    }
}

public sealed class Depot
{
    public string Street { get; set; } = "";
}

public class Nozzle
{
    public int Id { get; set; }

    public int Number { get; set; }
}

public sealed class IbcNozzle : Nozzle
{
    public int LitresLeft { get; set; }
}

/// <summary>TA's legacy schema, named the way its Prisma schema names it: camel-case columns, tables after the class.</summary>
public class LegacyContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Refuel> Refuels => Set<Refuel>();

    public DbSet<Tanker> Tankers => Set<Tanker>();

    public DbSet<Nozzle> Nozzles => Set<Nozzle>();

    public static TContext Open<TContext>(SqliteConnection connection, Func<DbContextOptions, TContext> create)
        where TContext : DbContext =>
        create(new DbContextOptionsBuilder<TContext>().UseSqlite(connection).Options);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // ConstraintNames FIRST: the names it builds must still read the columns as ColumnNames names them.
        configurationBuilder.ConstraintNames(n => n.Kind == ConstraintKind.Index ? $"{n.Table}_{n.Joined}_idx" : null);
        configurationBuilder.ColumnNames(n => n.CamelCase).TableNames(t => t.Class);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Refuel>(Configure);
        modelBuilder.Entity<Tanker>().OwnsOne(t => t.Depot);
        modelBuilder.Entity<Tanker>().OwnsMany(t => t.Legs);
        modelBuilder.Entity<Ledger>(ledger => KeptNames.Keep(ledger.Metadata.ClrType));
        modelBuilder.Entity<IbcNozzle>();
    }

    private static void Configure(EntityTypeBuilder<Refuel> builder)
    {
        builder.ToTable("Transaction");
        builder.Property(r => r.Moment).HasColumnName("dateTime");
        builder.ComplexProperty(r => r.Km);
        builder.ComplexProperty(r => r.Trip);
        builder.Columns(r => r.Trip, "{Member}Trip");
        builder.ComplexCollection(r => r.Stops, stops => stops.ToJson());
        builder.HasIndex(r => r.CountedAt);
    }
}

/// <summary>The same model in snake case, for a PostgreSQL-shaped schema.</summary>
public sealed class SnakeContext(DbContextOptions options) : LegacyContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.TableNames(t => t.SnakeCase).ColumnNames(n => n.SnakeCase);
}

/// <summary><c>ColumnNames</c> and <c>TableNames</c> name what the model left unnamed, and nothing it named itself.</summary>
[Collection(DataDbCollection.Name)]
public sealed class NamingConventionTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public NamingConventionTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void A_plain_property_is_named_in_camel_case()
    {
        var refuel = Model(Camel()).FindEntityType(typeof(Refuel))!;

        var columns = new[] { "Id", "TenantId", "CountedAt", "VATNumber", "TankerId" }.Select(p => Column(refuel, p));

        Assert.Equal(["id", "tenantId", "countedAt", "vatNumber", "tankerId"], columns);
    }

    [Fact]
    public void A_column_named_in_configure_keeps_its_name()
    {
        var refuel = Model(Camel()).FindEntityType(typeof(Refuel))!;

        var moment = Column(refuel, nameof(Refuel.Moment));

        Assert.Equal("dateTime", moment);
    }

    [Fact]
    public void A_table_takes_its_class_name_over_its_db_set()
    {
        var model = Model(Camel());

        var tables = new[] { typeof(Tanker), typeof(Nozzle) }.Select(t => model.FindEntityType(t)!.GetTableName());

        Assert.Equal(["Tanker", "Nozzle"], tables);
    }

    [Fact]
    public void A_table_named_in_configure_keeps_its_name()
    {
        var refuel = Model(Camel()).FindEntityType(typeof(Refuel))!;

        var table = refuel.GetTableName();

        Assert.Equal("Transaction", table);
    }

    [Fact]
    public void A_value_objects_column_is_named_after_its_whole_path()
    {
        var refuel = Model(Camel()).FindEntityType(typeof(Refuel))!;
        var store = StoreObjectIdentifier.Table("Transaction");

        var km = refuel.FindComplexProperty(nameof(Refuel.Km))!.ComplexType;
        var columns = new[] { "Start", "End" }.Select(p => km.FindProperty(p)!.GetColumnName(store));

        Assert.Equal(["kmStart", "kmEnd"], columns);
    }

    [Fact]
    public void A_value_object_named_by_a_columns_pattern_keeps_the_patterns_names()
    {
        var refuel = Model(Camel()).FindEntityType(typeof(Refuel))!;
        var store = StoreObjectIdentifier.Table("Transaction");

        var trip = refuel.FindComplexProperty(nameof(Refuel.Trip))!.ComplexType;
        var columns = new[] { "Start", "End" }.Select(p => trip.FindProperty(p)!.GetColumnName(store));

        Assert.Equal(["StartTrip", "EndTrip"], columns);
    }

    [Fact]
    public void A_json_column_is_named_and_what_it_holds_is_not()
    {
        var refuel = Model(Camel()).FindEntityType(typeof(Refuel))!;

        var stops = refuel.FindComplexProperty(nameof(Refuel.Stops))!.ComplexType;

        Assert.Equal("stops", stops.GetContainerColumnName());
        Assert.Equal("Place", stops.FindProperty(nameof(Stop.Place))!.GetJsonPropertyName());
    }

    [Fact]
    public void An_owned_type_in_its_owners_table_is_named_after_its_navigation_and_shares_its_owners_key()
    {
        var depot = Model(Camel()).FindEntityType(typeof(Depot))!;
        var store = StoreObjectIdentifier.Table("Tanker");

        var columns = depot.GetProperties().Select(p => p.GetColumnName(store)).Order(StringComparer.Ordinal);

        Assert.Equal(["depotStreet", "id"], columns);
    }

    [Fact]
    public void A_hierarchy_in_one_table_names_its_table_once_and_its_discriminator_too()
    {
        var model = Model(Camel());

        var ibc = model.FindEntityType(typeof(IbcNozzle))!;
        var columns = new[] { "Id", "Number", "LitresLeft", "Discriminator" }.Select(p => ibc.FindProperty(p)!.GetColumnName());

        Assert.Equal("Nozzle", ibc.GetTableName());
        Assert.Equal(["id", "number", "litresLeft", "discriminator"], columns);
    }

    [Fact]
    public void A_constraint_named_before_the_columns_still_reads_their_new_names()
    {
        var refuel = Model(Camel()).FindEntityType(typeof(Refuel))!;

        var index = refuel.GetIndexes().Single(i => i.Properties[0].Name == nameof(Refuel.CountedAt));

        Assert.Equal("Transaction_countedAt_idx", index.GetDatabaseName());
    }

    [Fact]
    public void Snake_case_names_every_step_of_a_path()
    {
        var model = Model(Snake());

        var refuel = model.FindEntityType(typeof(Refuel))!;
        var km = refuel.FindComplexProperty(nameof(Refuel.Km))!.ComplexType;

        Assert.Equal("tanker", model.FindEntityType(typeof(Tanker))!.GetTableName());
        Assert.Equal("vat_number", Column(refuel, nameof(Refuel.VATNumber)));
        Assert.Equal("km_start", km.FindProperty(nameof(Odometer.Start))!.GetColumnName(StoreObjectIdentifier.Table("Transaction")));
    }

    [Fact]
    public async Task A_database_created_from_the_model_holds_the_names_and_reads_back_through_them()
    {
        await using var db = Camel();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        db.Add(new Refuel { Id = "r1", TenantId = "t1", VATNumber = "HU1", Km = new Odometer(10, 42), Stops = [new Stop("Győr", 5)] });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var columns = await TableColumns("Transaction");
        var read = await db.Refuels.SingleAsync(r => r.Km.End == 42, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["EndTrip", "StartTrip", "countedAt", "dateTime", "id", "kmEnd", "kmStart", "stops", "tankerId", "tenantId", "vatNumber"],
            columns);
        Assert.Equal("Győr", read.Stops.Single().Place);
    }

    [Fact]
    public void A_table_a_battery_recorded_keeps_its_names()
    {
        var ledger = Model(Snake()).FindEntityType(typeof(Ledger))!;

        var names = (ledger.GetTableName(), Column(ledger, nameof(Ledger.EntryCount)));

        Assert.Equal(("Ledger", "EntryCount"), names);
    }

    [Fact]
    public void An_owned_type_in_a_table_of_its_own_is_named_after_its_class()
    {
        var legs = Model(Snake()).FindEntityType(typeof(Leg))!;

        var names = (legs.GetTableName(), Column(legs, nameof(Leg.DrivenKm)));

        Assert.Equal(("leg", "driven_km"), names);
    }

    [Fact]
    public void A_one_value_value_object_inside_another_is_renamed_like_its_siblings()
    {
        using var db = new CamelGondolaContext(
            new DbContextOptionsBuilder<CamelGondolaContext>().UseSqlite("Data Source=:memory:").Options);
        var gondola = Model(db).FindEntityType(typeof(Gondola))!;
        var store = StoreObjectIdentifier.Table("Gondolas");

        var crate = gondola.FindComplexProperty(nameof(Gondola.Crate))!.ComplexType;
        var route = gondola.FindComplexProperty(nameof(Gondola.Route))!.ComplexType.FindComplexProperty(nameof(GondolaRoute.Label))!.ComplexType;
        var columns = new[]
        {
            crate.FindProperty(nameof(GondolaCrate.Alias))!.GetColumnName(store),
            crate.FindProperty(nameof(GondolaCrate.Units))!.GetColumnName(store),
            route.FindProperty(nameof(GondolaLabel.Code))!.GetColumnName(store),
        };

        Assert.Equal(["crateAlias", "crateUnits", "routeLabelCode"], columns);
    }

    private static string? Column(IEntityType entityType, string property) =>
        entityType.FindProperty(property)!.GetColumnName(StoreObjectIdentifier.Table(entityType.GetTableName()!));

    private static IModel Model(DbContext db) => db.GetService<IDesignTimeModel>().Model;

    private LegacyContext Camel() => LegacyContext.Open(_connection, o => new LegacyContext(o));

    private SnakeContext Snake() => LegacyContext.Open(_connection, o => new SnakeContext(o));

    private async Task<List<string>> TableColumns(string table)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}') ORDER BY name";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var names = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
