using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// The domain model, unpolluted: three markers, and not one line of infrastructure on the class. The
// columns exist and behave; they are simply not properties of the type you wrote.
public sealed class Memo : Model<Guid>, ITimestamped, ISoftDeletable
{
    private Memo() { } // EF materialization

    public string Text { get; private set; } = "";

    public static Memo Write(string text) => new() { Id = Guid.NewGuid(), Text = text };

    public void Edit(string text) => Text = text;
}

[Collection(DataDbCollection.Name)]
public sealed class ShadowColumnTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-shadow-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_marker_alone_creates_the_column_without_a_property_on_the_class()
    {
        await using var database = await StartDatabaseAsync();

        var note = database.Context.Model.FindEntityType(typeof(Memo))!;

        foreach (var column in new[] { "CreatedAt", "UpdatedAt", "DeletedAt" })
        {
            var property = note.FindProperty(column);
            Assert.NotNull(property);
            Assert.True(property.IsShadowProperty(), $"{column} should be a shadow column on Memo");
        }

        // And the class really has none of them.
        Assert.Null(typeof(Memo).GetProperty("CreatedAt"));
        Assert.Null(typeof(Memo).GetProperty("DeletedAt"));
    }

    [Fact]
    public async Task The_audit_stamps_are_written_even_though_the_class_cannot_see_them()
    {
        await using var database = await StartDatabaseAsync();

        var note = Memo.Write("first");
        await note.SaveAsync();

        var created = await Memo.All.QueryAsync((q, ct) =>
            q.Select(n => EF.Property<DateTime>(n, "CreatedAt")).ToListAsync(ct));

        Assert.Single(created);
        Assert.NotEqual(default, created[0]);
    }

    [Fact]
    public async Task Soft_delete_still_hides_the_row_behind_a_shadow_DeletedAt()
    {
        await using var database = await StartDatabaseAsync();

        var note = Memo.Write("doomed");
        await note.SaveAsync();

        await note.DeleteAsync();

        Assert.Equal(0, await Memo.CountAsync());
        Assert.Equal(1, await Memo.IgnoreQueryFilters().CountAsync());

        // The stamp is on the row, reachable through EF.Property when something actually needs it.
        var deletedAt = await Memo.All.IgnoreQueryFilters().QueryAsync((q, ct) =>
            q.Select(n => EF.Property<DateTime?>(n, "DeletedAt")).ToListAsync(ct));

        Assert.NotNull(deletedAt[0]);
    }

    [Fact]
    public void IVersioned_is_the_one_marker_that_needs_a_declared_property()
    {
        // Not an arbitrary restriction: optimistic concurrency exists to round-trip the token through an
        // edit form, and a value the application cannot read is a value it cannot send back. EF's own
        // failure is a "0 rows affected" naming neither the token nor the reason, so this is caught while
        // the model is being built, and says what to add.
        using var context = new VersionedContext(
            new DbContextOptionsBuilder<VersionedContext>().UseSqlite($"Data Source={_dbPath}").Options);

        var error = Assert.Throws<InvalidOperationException>(() => context.Model.GetEntityTypes().ToList());

        Assert.Contains(nameof(Ledger), error.Message, StringComparison.Ordinal);
        Assert.Contains("public int Version", error.Message, StringComparison.Ordinal);
    }

    // Deliberately NOT a Model: the generator maps every Model in the assembly into the shared model, so
    // an intentionally broken one would fail every other test in the suite. IVersioned applies to any
    // mapped entity, which is what lets this be tested in a context of its own.
    private sealed class Ledger : IVersioned
    {
        public Guid Id { get; set; }
    }

    private sealed class VersionedContext(DbContextOptions<VersionedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Ledger>();
            modelBuilder.ApplyRaskConventions();
        }
    }

    [Fact]
    public async Task SaveAsync_still_tells_an_insert_from_an_update_without_a_readable_CreatedAt()
    {
        // A shadow CreatedAt lives in the change tracker, so a detached model has none to read — this is
        // the path that asks the database instead. Both directions have to come out right.
        await using var database = await StartDatabaseAsync();

        var note = Memo.Write("once");
        await note.SaveAsync();
        Assert.Equal(1, await Memo.CountAsync());

        await note.SaveAsync();
        Assert.Equal(1, await Memo.CountAsync());

        var loaded = await Memo.FirstOrDefaultAsync(n => n.Text == "once");
        loaded!.Edit("twice");
        await loaded.SaveAsync();

        Assert.Equal(1, await Memo.CountAsync());
        Assert.Equal(1, await Memo.CountAsync(n => n.Text == "twice"));
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"));
}
