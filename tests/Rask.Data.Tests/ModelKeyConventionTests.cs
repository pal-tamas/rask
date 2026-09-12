using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests;

// A Guid-keyed entity with one factory that assigns its id and one that forgets to, saved the way a domain
// operation saves — through a context, not the generated writes.
public sealed class Voucher : Model<Guid>
{
    private Voucher() { }

    public string Code { get; private set; } = "";

    public static Voucher Issue(string code) => new() { Id = Guid.CreateVersion7(), Code = code };

    public static Voucher WithoutId(string code) => new() { Code = code };
}

[Collection(DataDbCollection.Name)]
public sealed class ModelKeyConventionTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-keys-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"));

    [Fact]
    public async Task An_entity_added_with_its_Guid_key_unassigned_is_refused_rather_than_inserted_empty()
    {
        await using var database = await StartDatabaseAsync();

        database.Context.Add(Voucher.WithoutId("EMPTY"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => database.Context.SaveChangesAsync());
        Assert.Contains("'Voucher'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Guid.CreateVersion7()", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_entity_that_assigns_its_own_key_is_inserted_with_it()
    {
        await using var database = await StartDatabaseAsync();
        var voucher = Voucher.Issue("OK");

        database.Context.Add(voucher);
        await database.Context.SaveChangesAsync();

        Assert.NotNull(await Voucher.FindAsync(voucher.Id));
    }

    [Fact]
    public void A_key_nobody_configured_is_never_generated()
    {
        using var context = new PlainContext(Options<PlainContext>());

        Assert.Equal(ValueGenerated.Never, KeyOf(context).ValueGenerated);
    }

    [Fact]
    public void A_key_the_application_configured_as_generated_is_left_as_it_was()
    {
        // An app with a context of its own calls ApplyRaskConventions AFTER its configuration, so the
        // convention must not overwrite what it was told.
        using var context = new OwnKeyContext(Options<OwnKeyContext>());

        Assert.Equal(ValueGenerated.OnAdd, KeyOf(context).ValueGenerated);
    }

    private static DbContextOptions<TContext> Options<TContext>()
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>().UseSqlite("Data Source=:memory:").Options;

    private static IProperty KeyOf(DbContext context) =>
        context.Model.FindEntityType(typeof(Voucher))!.FindProperty(nameof(Voucher.Id))!;

    private sealed class PlainContext(DbContextOptions<PlainContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Voucher>();
            modelBuilder.ApplyRaskConventions();
        }
    }

    private sealed class OwnKeyContext(DbContextOptions<OwnKeyContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Voucher>().Property(v => v.Id).ValueGeneratedOnAdd();
            modelBuilder.ApplyRaskConventions();
        }
    }
}
