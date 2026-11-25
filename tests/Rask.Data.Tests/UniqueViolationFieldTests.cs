using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Cqrs;

namespace Rask.Data.Tests;

/// <summary>An aggregate whose children carry a number unique across every armory.</summary>
public sealed class Armory : Aggregate<int, Model, Deletable>
{
    private readonly List<Pistol> _pistols = [];

    private Armory() { }

    public string Name { get; private set; } = "";

    public IReadOnlyList<Pistol> Pistols => _pistols;

    public static Armory Named(string name) => new() { Name = name };

    public Armory Holding(string number)
    {
        _pistols.Add(Pistol.Numbered(number));
        return this;
    }
}

public sealed class Pistol : Entity<int>
{
    public const string NumberTaken = "A pistol with this number is already registered.";

    private Pistol() { }

    public string Number { get; private set; } = "";

    internal static Pistol Numbered(string number) => new() { Number = number };

    public static void Configure(EntityTypeBuilder<Pistol> builder) =>
        builder.HasIndex(p => p.Number).IsUnique(NumberTaken);
}

/// <summary>An aggregate whose unique column is a normalised copy of what the form edits.</summary>
public sealed class PlateLocker : Aggregate<int, Model, Deletable>
{
    public const string PlateTaken = "A locker with this plate already exists.";

    private PlateLocker() { }

    public string Plate { get; private set; } = "";

    public string NormalizedPlate { get; private set; } = "";

    public static PlateLocker Plated(string plate) => new() { Plate = plate, NormalizedPlate = plate.ToUpperInvariant() };

    public static void Configure(EntityTypeBuilder<PlateLocker> builder) =>
        builder.HasIndex(l => l.NormalizedPlate).IsUnique(PlateTaken).Field(nameof(Plate));
}

/// <summary>A plain EF Core entity whose unique index is over a member of a complex property.</summary>
public sealed class Logsheet
{
    public const string StartTaken = "Another waybill starts at this reading.";

    public int Id { get; set; }

    public OdometerSpan Km { get; set; } = new();
}

public sealed class OdometerSpan
{
    public int Start { get; set; }

    public int End { get; set; }
}

public sealed class LogsheetContext(DbContextOptions<LogsheetContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Logsheet>(b =>
        {
            b.ComplexProperty(w => w.Km);
            b.HasIndex(w => w.Km.Start).IsUnique(Logsheet.StartTaken);
        });
}

/// <summary>
/// A unique index's violation lands under the field a form shows: the one <c>Field("…")</c> names, a complex
/// property's member by its path, and a child row of an aggregate under its row.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class UniqueViolationFieldTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    public void Dispose() => _sqlite.Dispose();

    [Fact]
    public async Task A_collision_on_a_normalised_column_is_shown_under_the_field_it_names()
    {
        await using var database = await StartDatabaseAsync();
        await PlateLocker.Plated("abc-1").Save(cancellationToken: Cancellation);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => PlateLocker.Plated("ABC-1").Save(cancellationToken: Cancellation));

        Assert.Equal(["Plate"], Assert.Single(refused.Failures).Fields);
        Assert.Equal([PlateLocker.PlateTaken], refused.Errors["Plate"]);
    }

    [Fact]
    public async Task A_collision_in_a_child_row_is_shown_under_that_row()
    {
        await using var database = await StartDatabaseAsync();
        await Armory.Named("North").Holding("P-1").Save(cancellationToken: Cancellation);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Armory.Named("South").Holding("P-2").Holding("P-1").Save(cancellationToken: Cancellation));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Pistol.NumberTaken, failure.Message);
        Assert.Contains("Pistols[1].Number", failure.Fields);
        Assert.All(failure.Fields, field => Assert.Matches(@"^Pistols\[\d\]\.Number$", field));
    }

    [Fact]
    public async Task A_collision_on_a_complex_propertys_member_is_shown_under_its_path()
    {
        await using var db = new LogsheetContext(OptionsLayout.LikeAddDbContext<LogsheetContext>()
            .UseSqlite(_sqlite.ConnectionString)
            .AddInterceptors(new ViolationMessageInterceptor())
            .Options);
        await db.Database.EnsureCreatedAsync(Cancellation);
        db.Add(new Logsheet { Km = new OdometerSpan { Start = 100, End = 200 } });
        await db.SaveChangesAsync(Cancellation);

        db.Add(new Logsheet { Km = new OdometerSpan { Start = 100, End = 300 } });
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => db.SaveChangesAsync(Cancellation));

        Assert.Equal(["Km.Start"], Assert.Single(refused.Failures).Fields);
    }

    [Fact]
    public async Task A_collision_an_execute_update_makes_is_shown_under_the_indexs_field()
    {
        await using var db = new LogsheetContext(OptionsLayout.LikeAddDbContext<LogsheetContext>()
            .UseSqlite(_sqlite.ConnectionString)
            .AddInterceptors(new ViolationMessageInterceptor())
            .Options);
        await db.Database.EnsureCreatedAsync(Cancellation);
        db.AddRange(new Logsheet { Km = new OdometerSpan { Start = 100, End = 200 } }, new Logsheet { Km = new OdometerSpan { Start = 300, End = 400 } });
        await db.SaveChangesAsync(Cancellation);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => db.Set<Logsheet>()
            .Where(w => w.Km.Start == 300)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Km.Start, 100), Cancellation));

        Assert.Equal(Logsheet.StartTaken, Assert.Single(refused.Failures).Message);
        Assert.Equal(["Km.Start"], refused.Failures[0].Fields);
    }

    [Fact]
    public void The_field_is_recorded_on_the_index_and_its_failure_names_only_it()
    {
        var index = new ModelBuilder().Entity<Shelf>().HasIndex(s => new { s.Aisle, s.Label });

        index.IsUnique("Taken.").Field("Label");

        Assert.Equal(["Label"], UniqueViolation.FailureOf(index.Metadata).Fields);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_field_that_names_nothing_is_refused(string blank)
    {
        var index = new ModelBuilder().Entity<Shelf>().HasIndex(s => s.Label);

        var refused = Assert.ThrowsAny<ArgumentException>(() => index.Field(blank));

        Assert.Equal("field", refused.ParamName);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString));
}
