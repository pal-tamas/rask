using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests.Fuel;

/// <summary>A delivery note of an existing app: numbered tenants, read only, no framework columns.</summary>
public sealed class DeliveryNote : Aggregate<int, PerTenant>
{
    public int? TenantId { get; private set; }

    public string Number { get; private set; } = "";

    public IReadOnlyList<DeliveryNoteTransaction> Fuellings { get; private set; } = [];

    public static void Configure(EntityTypeBuilder<DeliveryNote> builder) => builder.ToTable("DeliveryNotes");
}

/// <summary>A fuel transaction of the same app.</summary>
public sealed class Transaction : Aggregate<int, PerTenant>
{
    public int? TenantId { get; private set; }

    public int ProductId { get; private set; }

    public static void Configure(EntityTypeBuilder<Transaction> builder) => builder.ToTable("Transactions");
}

/// <summary>Which transactions fuelled a note, keyed by the pair: no Id, no TenantId, no stamps.</summary>
public sealed partial class DeliveryNoteTransaction : Entity<(DeliveryNote, Transaction)>
{
    public decimal Quantity { get; private set; }

    public static void Configure(EntityTypeBuilder<DeliveryNoteTransaction> builder)
    {
        builder.ToTable("DeliveryNoteTransactions");
        builder.Property(t => t.Quantity).HasPrecision(18, 9);
    }
}

/// <summary>The context the read tests open.</summary>
public sealed class FuelContext(DbContextOptions<FuelContext> options) : RaskDbContext(options);

/// <summary>A context of the app's own, wired by hand: it maps the tables and asks for Rask's conventions.</summary>
public sealed class HandWiredFuelContext(DbContextOptions<HandWiredFuelContext> options) : DbContext(options), ITenantScoped
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeliveryNote>().ToTable("DeliveryNotes");
        modelBuilder.Entity<Transaction>().ToTable("Transactions");
        modelBuilder.Entity<DeliveryNoteTransaction>().ToTable("DeliveryNoteTransactions");
        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>
/// An entity keyed by several columns, declared as a tuple of types, read over a table an existing app made: the key
/// parts, a filter on one of them, the navigation to the other aggregate, the tenant through the note, and the
/// <c>Include</c> from the note.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class CompositeKeyReadTests : IAsyncLifetime
{
    private const int Acme = 7;
    private const int Globex = 8;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-composite-key-{Guid.NewGuid():N}.db");
    private ServiceProvider _app = null!;

    public async ValueTask InitializeAsync()
    {
        var connection = $"Data Source={_dbPath};Pooling=False";
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData<FuelContext>();
        services.AddDbContextFactory<FuelContext>((sp, o) => o
            .UseSqlite(connection)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite(connection));

        _app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(_app);

        await using var context = await OpenAsync();
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "DeliveryNotes" ("Id" INTEGER NOT NULL PRIMARY KEY, "Number" TEXT NOT NULL, "TenantId" INTEGER NULL);
            CREATE TABLE "Transactions" ("Id" INTEGER NOT NULL PRIMARY KEY, "ProductId" INTEGER NOT NULL, "TenantId" INTEGER NULL);
            CREATE TABLE "DeliveryNoteTransactions" (
                "DeliveryNoteId" INTEGER NOT NULL REFERENCES "DeliveryNotes" ("Id"),
                "TransactionId" INTEGER NOT NULL REFERENCES "Transactions" ("Id"),
                "Quantity" TEXT NOT NULL,
                PRIMARY KEY ("DeliveryNoteId", "TransactionId"));
            INSERT INTO "DeliveryNotes" VALUES (1, 'A-1', 7), (2, 'A-2', 7), (3, 'G-1', 8);
            INSERT INTO "Transactions" VALUES (10, 100, 7), (11, 101, 7), (12, 102, 8);
            INSERT INTO "DeliveryNoteTransactions" VALUES (1, 10, '12.5'), (1, 11, '3.25'), (2, 11, '1'), (3, 12, '40');
            """,
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Db.Reset();
        await _app.DisposeAsync();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task The_table_is_keyed_by_its_parts_in_the_order_the_tuple_names_them()
    {
        await using var context = await OpenAsync();

        var key = context.Model.FindEntityType(typeof(DeliveryNoteTransaction))!.FindPrimaryKey()!;

        Assert.Equal(["DeliveryNoteId", "TransactionId"], key.Properties.Select(static p => p.Name));
        Assert.All(key.Properties, static p => Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never, p.ValueGenerated));
    }

    [Fact]
    public void A_context_wired_by_hand_keys_the_table_by_the_same_parts()
    {
        using var context = new HandWiredFuelContext(new DbContextOptionsBuilder<HandWiredFuelContext>()
            .UseSqlite($"Data Source={_dbPath}").Options);

        var entity = context.Model.FindEntityType(typeof(DeliveryNoteTransaction))!;

        Assert.Equal(["DeliveryNoteId", "TransactionId"], entity.FindPrimaryKey()!.Properties.Select(static p => p.Name));
        Assert.Null(entity.FindProperty("Id"));
    }

    [Fact]
    public async Task The_read_face_reads_each_row_with_its_key_parts_and_the_transactions_product()
    {
        int[] noteIds = [1, 2];

        List<(int, int, decimal, int)> rows;
        using (Tenant.Use(Acme))
        {
            rows = [.. (await DeliveryNoteTransaction.Where(t => noteIds.Contains(t.DeliveryNoteId))
                    .Select(t => new { t.DeliveryNoteId, t.TransactionId, t.Quantity, t.Transaction.ProductId }))
                .OrderBy(static r => r.DeliveryNoteId).ThenBy(static r => r.TransactionId)
                .Select(static r => (r.DeliveryNoteId, r.TransactionId, r.Quantity, r.ProductId))];
        }

        Assert.Equal([(1, 10, 12.5m, 100), (1, 11, 3.25m, 101), (2, 11, 1m, 101)], rows);
    }

    [Fact]
    public async Task A_filter_on_one_key_part_reads_only_the_rows_holding_that_value()
    {
        List<int> notes;
        using (Tenant.Use(Acme))
        {
            notes = await DeliveryNoteTransaction.Where(t => t.TransactionId == 11).OrderBy(t => t.DeliveryNoteId)
                .Select(t => t.DeliveryNoteId);
        }

        Assert.Equal([1, 2], notes);
    }

    [Fact]
    public async Task The_navigation_back_to_the_note_reads_its_number()
    {
        List<string> numbers;
        using (Tenant.Use(Acme))
        {
            numbers = await DeliveryNoteTransaction.Where(t => t.TransactionId == 10).Select(t => t.DeliveryNote.Number);
        }

        Assert.Equal(["A-1"], numbers);
    }

    [Fact]
    public async Task Rows_under_another_tenants_note_are_not_seen()
    {
        int count;
        int theirs;
        using (Tenant.Use(Acme))
        {
            count = await DeliveryNoteTransaction.Count(TestContext.Current.CancellationToken);
            theirs = await DeliveryNoteTransaction.Count(t => t.DeliveryNoteId == 3, TestContext.Current.CancellationToken);
        }

        Assert.Equal(3, count);
        Assert.Equal(0, theirs);
    }

    [Fact]
    public async Task Including_the_fuellings_from_the_note_loads_them_with_their_key_parts()
    {
        DeliveryNoteRead? note;
        using (Tenant.Use(Acme))
        {
            note = await DeliveryNote.Include(n => n.Fuellings).First(n => n.Id == 1, TestContext.Current.CancellationToken);
        }

        Assert.NotNull(note);
        Assert.Equal([10, 11], note.Fuellings.Select(static f => f.TransactionId).Order());
        Assert.All(note.Fuellings, static f => Assert.Equal(1, f.DeliveryNoteId));
    }

    [Fact]
    public async Task Writing_a_row_keyed_by_several_columns_is_refused_while_writes_are_not_supported()
    {
        await using var context = await OpenAsync();
        context.Add(new DeliveryNoteTransaction());

        var refused = await Assert.ThrowsAsync<NotSupportedException>(
            () => context.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Contains("'DeliveryNoteTransaction' was added", refused.Message, StringComparison.Ordinal);
        Assert.Contains("read-only", refused.Message, StringComparison.Ordinal);
    }

    private Task<FuelContext> OpenAsync() =>
        _app.GetRequiredService<IDbContextFactory<FuelContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);
}
