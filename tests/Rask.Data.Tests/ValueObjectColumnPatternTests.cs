using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests.Odometer;

/// <summary>A delivery note whose kilometre range lives in the columns an older app named StartKm and EndKm.</summary>
public sealed class OdometerNote : Aggregate<int>
{
    private OdometerNote() { }

    public RigKm Km { get; private set; } = null!;

    public static OdometerNote Of(int start, int end) => new() { Km = RigKm.Create(start, end, nameof(Km)) };

    public static void Configure(EntityTypeBuilder<OdometerNote> builder)
    {
        builder.ToTable("OdometerNotes");
        builder.Columns(n => n.Km, "{Member}Km");
    }
}

/// <summary>The context the odometer tests open.</summary>
public sealed class OdometerContext(DbContextOptions<OdometerContext> options) : RaskDbContext(options);

/// <summary>
/// <c>b.Columns(n =&gt; n.Km, "{Member}Km")</c>: a value object of several members stored in columns named after a
/// pattern — the schema, a round trip through a table made the old way, and the read face under the same names.
/// </summary>
public sealed class ValueObjectColumnPatternTests : IAsyncLifetime
{
    private readonly InMemorySqlite _sqlite = new();
    private ServiceProvider _app = null!;

    // The model surface pointed at this class's app for the test's flow alone (Db.Use): a constructor runs in the flow
    // its test does, which InitializeAsync, being async, does not hand on.
    private readonly IDisposable _db;

    public ValueObjectColumnPatternTests()
    {
        var connection = _sqlite.ConnectionString;
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData<OdometerContext>();
        services.AddDbContextFactory<OdometerContext>((sp, o) => o
            .UseSqlite(connection)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite(connection));

        _app = services.BuildServiceProvider(validateScopes: true);
        _db = Db.Use(_app);
    }

    public async ValueTask InitializeAsync()
    {
        await using var context = await Factory().CreateDbContextAsync(TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "OdometerNotes" (
                "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, "StartKm" INTEGER NOT NULL, "EndKm" INTEGER NOT NULL);
            INSERT INTO "OdometerNotes" ("StartKm", "EndKm") VALUES (100, 180), (5, 9);
            """,
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _db.Dispose();
        await _app.DisposeAsync();
        _sqlite.Dispose();
    }

    [Fact]
    public async Task The_value_objects_columns_take_the_patterns_names()
    {
        await using var context = await Factory().CreateDbContextAsync(TestContext.Current.CancellationToken);

        var km = context.Model.FindEntityType(typeof(OdometerNote))!.FindComplexProperty(nameof(OdometerNote.Km))!.ComplexType;

        Assert.Equal(["EndKm", "StartKm"], km.GetProperties().Select(p => p.GetColumnName()).Order());
    }

    [Fact]
    public async Task A_note_round_trips_through_the_columns_the_old_table_has()
    {
        var token = TestContext.Current.CancellationToken;
        var note = OdometerNote.Of(200, 260);
        await note.Save(cancellationToken: token);
        await using var context = await Factory().CreateDbContextAsync(token);

        var stored = await context.Set<OdometerNote>().AsNoTracking().SingleAsync(n => n.Id == note.Id, token);
        var raw = await context.Database
            .SqlQuery<int>($"""SELECT "EndKm" AS "Value" FROM "OdometerNotes" WHERE "Id" = {note.Id}""")
            .SingleAsync(token);

        Assert.Equal(RigKm.Create(200, 260), stored.Km);
        Assert.Equal(260, raw);
    }

    [Fact]
    public async Task The_read_face_filters_and_sorts_by_the_patterns_names()
    {
        var ends = await OdometerNote.Where(n => n.StartKm >= 5).OrderBy(n => n.StartKm).Select(n => n.EndKm);

        Assert.Equal([9, 180], ends);
    }

    [Fact]
    public void A_pattern_without_the_member_placeholder_is_refused()
    {
        var builder = new ModelBuilder().Entity<OdometerNote>();

#pragma warning disable RASK165 // the run-time refusal is what is tested, on a builder outside Configure on purpose
        var refused = Record.Exception(() => builder.Columns(n => n.Km, "Km"));
#pragma warning restore RASK165

        Assert.IsType<ArgumentException>(refused);
        Assert.Contains("{Member}", refused.Message, StringComparison.Ordinal);
    }

    private IDbContextFactory<OdometerContext> Factory() => _app.GetRequiredService<IDbContextFactory<OdometerContext>>();
}
