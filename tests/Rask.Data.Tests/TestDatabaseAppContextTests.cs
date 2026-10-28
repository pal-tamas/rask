using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>A row only the app's own context maps — what a battery's <c>AddRaskOutbox()</c> table stands for.</summary>
public sealed class LedgerNote
{
    public int Id { get; set; }

    public string Text { get; set; } = "";
}

/// <summary>An app's own context: a table of its own in <c>OnModelCreating</c>, and a guard in <c>SaveChangesAsync</c>.</summary>
public sealed class LedgerContext(DbContextOptions<LedgerContext> options) : RaskDbContext(options)
{
    public const string Refused = "refused";

    public DbSet<LedgerNote> Notes => Set<LedgerNote>();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (ChangeTracker.Entries<Widget>().Any(e => e.Entity.Name == Refused))
        {
            throw new InvalidOperationException("The ledger refuses that widget.");
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<LedgerNote>();
    }
}

/// <summary>A context the app builds some other way, so there is no constructor taking its options.</summary>
public sealed class OptionlessContext() : RaskDbContext(new DbContextOptions<RaskDbContext>());

[Collection(DataDbCollection.Name)]
public sealed class TestDatabaseAppContextTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-appctx-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task The_app_context_creates_the_table_its_own_model_adds()
    {
        await using var database = await StartLedgerAsync();

        database.Context.Notes.Add(new LedgerNote { Text = "opened" });
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.IsType<LedgerContext>(database.Context);
        Assert.Equal(["opened"], await database.Context.Notes.Select(n => n.Text).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_app_contexts_save_override_refuses_a_row_saved_through_the_fixture_context()
    {
        await using var database = await StartLedgerAsync();

        database.Context.Add(Widget.Create(LedgerContext.Refused));
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal("The ledger refuses that widget.", refused.Message);
    }

    [Fact]
    public async Task The_app_contexts_save_override_refuses_a_row_saved_through_an_aggregate()
    {
        await using var database = await StartLedgerAsync();
        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);
        var found = (await Widget.Find(widget.Id, cancellationToken: TestContext.Current.CancellationToken))!;

        found.Rename(LedgerContext.Refused);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => found.Save(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("The ledger refuses that widget.", refused.Message);
        Assert.Equal("anvil", (await database.Load<Widget>(widget.Id, TestContext.Current.CancellationToken))!.Name);
    }

    [Fact]
    public async Task The_app_context_keeps_the_audit_stamps_and_the_read_side()
    {
        await using var database = await StartLedgerAsync();

        var widget = await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(default, (await database.Load<Widget>(widget.Id, TestContext.Current.CancellationToken))!.CreatedAt);
        Assert.Equal(1, await Widget.Count(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_plain_start_still_runs_a_plain_rask_context()
    {
        await using var database = await TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"), cancellationToken: TestContext.Current.CancellationToken);

        await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(typeof(RaskDbContext), database.Context.GetType());
        Assert.Equal(1, await Widget.Count(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_context_without_a_constructor_taking_its_options_is_refused_by_name()
    {
        var start = () => TestDatabase.Start<OptionlessContext>(o => o.UseSqlite($"Data Source={_dbPath}"), cancellationToken: TestContext.Current.CancellationToken);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(start);

        Assert.Contains("public OptionlessContext(DbContextOptions<OptionlessContext> options) : base(options)", refused.Message, StringComparison.Ordinal);
    }

    private Task<TestDatabase<LedgerContext>> StartLedgerAsync() =>
        TestDatabase.Start<LedgerContext>(o => o.UseSqlite($"Data Source={_dbPath}"), cancellationToken: TestContext.Current.CancellationToken);
}
