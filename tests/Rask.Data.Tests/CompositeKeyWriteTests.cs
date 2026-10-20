using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data.Tests.Fuel;
using Rask.Data.Tests.Reports;

namespace Rask.Data.Tests;

/// <summary>
/// Rows keyed by several columns written through their aggregate: the entity's own factory makes them, the aggregate
/// refills its collection, and the save matches the new instances to the stored rows by key — an UPDATE of what
/// changed, an INSERT for a new key, a DELETE for a dropped one and nothing for the rest, at any depth.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class CompositeKeyWriteTests : IAsyncLifetime
{
    private const int Acme = 7;
    private const int Globex = 8;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-composite-write-{Guid.NewGuid():N}.db");
    private readonly SqlCommandLog _log = new();
    private ServiceProvider _app = null!;

    public async ValueTask InitializeAsync()
    {
        var connection = $"Data Source={_dbPath};Pooling=False";
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData<FuelContext>();
        services.AddDbContextFactory<FuelContext>((sp, o) => o
            .UseSqlite(connection)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>())
            .AddInterceptors(_log));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite(connection));

        _app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(_app);

        await using var context = await OpenAsync();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Db.Reset();
        await _app.DisposeAsync();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task A_new_note_saved_from_its_factory_inserts_its_fuellings_under_its_new_key()
    {
        var note = await IssueAsync(Acme, "A-1", (10, 12.5m), (11, 3.25m));

        var stored = await FuellingsAsync(Acme, note.Id);

        Assert.NotEqual(0, note.Id);
        Assert.Equal([(10, 12.5m), (11, 3.25m)], stored);
        Assert.All(note.Fuellings, f => Assert.Equal(note.Id, f.DeliveryNoteId));
    }

    [Fact]
    public async Task A_refilled_note_updates_the_changed_row_inserts_the_new_key_deletes_the_dropped_one_and_skips_the_rest()
    {
        var note = await IssueAsync(Acme, "A-1", (10, 12.5m), (11, 3.25m), (13, 2m));
        _log.Clear();

        using (Tenant.Use(Acme))
        {
            await DeliveryNote.Update(note.Id, n => n.Fill([(10, 12.5m), (11, 4m), (12, 1m)]),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        var writes = Writes();
        Assert.Equal(3, writes.Count);
        Assert.Single(writes, static w => w.StartsWith("UPDATE \"DeliveryNoteTransactions\" SET \"Quantity\" = @p", StringComparison.Ordinal)
                                          && w.Contains("WHERE \"DeliveryNoteId\" = @p", StringComparison.Ordinal)
                                          && w.Contains("AND \"TransactionId\" = @p", StringComparison.Ordinal));
        Assert.Single(writes, static w => w.StartsWith("INSERT INTO \"DeliveryNoteTransactions\" (\"DeliveryNoteId\", \"TransactionId\", \"Quantity\")", StringComparison.Ordinal));
        Assert.Single(writes, static w => w.StartsWith("DELETE FROM \"DeliveryNoteTransactions\"", StringComparison.Ordinal)
                                          && w.Contains("WHERE \"DeliveryNoteId\" = @p", StringComparison.Ordinal)
                                          && w.Contains("AND \"TransactionId\" = @p", StringComparison.Ordinal));
        Assert.Equal([(10, 12.5m), (11, 4m), (12, 1m)], await FuellingsAsync(Acme, note.Id));
    }

    [Fact]
    public async Task A_note_refilled_with_the_rows_it_holds_writes_nothing()
    {
        var note = await IssueAsync(Acme, "A-1", (10, 12.5m), (11, 3.25m));
        _log.Clear();

        using (Tenant.Use(Acme))
        {
            await DeliveryNote.Update(note.Id, n => n.Fill([(10, 12.5m), (11, 3.25m)]),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Empty(Writes());
        Assert.Equal([(10, 12.5m), (11, 3.25m)], await FuellingsAsync(Acme, note.Id));
    }

    [Fact]
    public async Task Each_replaced_row_is_tracked_as_what_the_save_does_with_it()
    {
        var note = await IssueAsync(Acme, "A-1", (10, 12.5m), (11, 3.25m), (13, 2m));
        await using var db = await OpenAsync();

        Dictionary<int, EntityState> states;
        using (Tenant.Use(Acme))
        {
            await DeliveryNote.Update(note.Id, n => n.Fill([(10, 12.5m), (11, 4m), (12, 1m)]), db: db,
                cancellationToken: TestContext.Current.CancellationToken);
            states = db.ChangeTracker.Entries<DeliveryNoteTransaction>()
                .ToDictionary(static e => e.Entity.TransactionId, static e => e.State);
        }

        Assert.Equal(EntityState.Unchanged, states[10]);
        Assert.Equal(EntityState.Modified, states[11]);
        Assert.Equal(EntityState.Added, states[12]);
        Assert.Equal(EntityState.Deleted, states[13]);
    }

    [Fact]
    public async Task A_note_found_refilled_and_saved_syncs_its_fuellings_by_key()
    {
        var issued = await IssueAsync(Acme, "A-1", (10, 12.5m), (11, 3.25m), (13, 2m));
        _log.Clear();

        using (Tenant.Use(Acme))
        {
            var note = await DeliveryNote.Find(issued.Id, cancellationToken: TestContext.Current.CancellationToken);
            note!.Fill([(10, 12.5m), (11, 4m), (12, 1m)]);
            await note.Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        var writes = Writes();
        Assert.Equal(3, writes.Count);
        Assert.Single(writes, static w => w.StartsWith("UPDATE \"DeliveryNoteTransactions\" SET \"Quantity\"", StringComparison.Ordinal));
        Assert.Single(writes, static w => w.StartsWith("INSERT INTO \"DeliveryNoteTransactions\"", StringComparison.Ordinal));
        Assert.Single(writes, static w => w.StartsWith("DELETE FROM \"DeliveryNoteTransactions\"", StringComparison.Ordinal));
        Assert.Equal([(10, 12.5m), (11, 4m), (12, 1m)], await FuellingsAsync(Acme, issued.Id));
    }

    [Fact]
    public async Task A_relaid_report_syncs_its_columns_and_each_columns_totals_by_key()
    {
        var report = await TitleAsync(Acme, "Fuel", [1, 2], [(1, 0, true, [100, 101]), (2, 1, false, []), (3, 2, false, [100])]);
        _log.Clear();

        using (Tenant.Use(Acme))
        {
            await Report.Update(report.Id, r => r.Lay([(1, 0, true, [101, 102]), (2, 5, false, []), (4, 3, true, [100])]),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        var writes = Writes();
        Assert.Single(writes, static w => w.StartsWith("UPDATE \"ReportColumn\" SET \"Order\"", StringComparison.Ordinal));
        Assert.Equal(2, writes.Count(static w => w.StartsWith("INSERT INTO \"ReportGroupTotal\"", StringComparison.Ordinal)));
        Assert.Equal(2, writes.Count(static w => w.StartsWith("DELETE FROM \"ReportGroupTotal\"", StringComparison.Ordinal)));
        Assert.Single(writes, static w => w.StartsWith("INSERT INTO \"ReportColumn\"", StringComparison.Ordinal));
        Assert.Single(writes, static w => w.StartsWith("DELETE FROM \"ReportColumn\"", StringComparison.Ordinal));
        Assert.DoesNotContain(writes, static w => w.Contains("\"ReportSite\"", StringComparison.Ordinal));
        Assert.Equal(
            ["1:0:True:101,102", "2:5:False:", "4:3:True:100"],
            await LayoutAsync(Acme, report.Id));
    }

    [Fact]
    public async Task A_grandchild_is_keyed_by_its_columns_key_and_its_own_part()
    {
        await using var context = await OpenAsync();

        var key = context.Model.FindEntityType(typeof(ReportGroupTotal))!.FindPrimaryKey()!;
        var parent = Assert.Single(context.Model.FindEntityType(typeof(ReportGroupTotal))!.GetForeignKeys());

        Assert.Equal(["ReportColumnReportId", "ReportColumnColumnId", "TotalColumnId"], key.Properties.Select(static p => p.Name));
        Assert.Equal(typeof(ReportColumn), parent.PrincipalEntityType.ClrType);
        Assert.Equal(["ReportColumnReportId", "ReportColumnColumnId"], parent.Properties.Select(static p => p.Name));
    }

    [Fact]
    public async Task A_report_of_numbered_tenants_keeps_its_rows_from_every_other_tenant()
    {
        var report = await TitleAsync(Acme, "Fuel", [1], [(1, 0, true, [100])]);
        await TitleAsync(Globex, "Theirs", [2], [(1, 0, true, [100, 101])]);

        int ours;
        int theirs;
        using (Tenant.Use(Globex))
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() => Report.Update(report.Id, r => r.Cover([9]),
                cancellationToken: TestContext.Current.CancellationToken));
            theirs = await ReportGroupTotal.Count(cancellationToken: TestContext.Current.CancellationToken);
        }

        using (Tenant.Use(Acme))
        {
            ours = await ReportGroupTotal.Count(cancellationToken: TestContext.Current.CancellationToken);
        }

        await using var context = await OpenAsync();
        Assert.Null(context.Model.FindEntityType(typeof(ReportGroupTotal))!.FindProperty(Columns.TenantId));
        Assert.Equal(2, theirs);
        Assert.Equal(1, ours);
        Assert.Equal([1], await SitesAsync(Acme, report.Id));
    }

    [Fact]
    public async Task Two_aggregates_refilled_in_one_shared_context_are_saved_together()
    {
        var note = await IssueAsync(Acme, "A-1", (10, 12.5m), (11, 3.25m));
        var report = await TitleAsync(Acme, "Fuel", [1, 2], [(1, 0, true, [100])]);
        _log.Clear();

        using (Tenant.Use(Acme))
        {
            await using var db = await OpenAsync();
            await DeliveryNote.Update(note.Id, n => n.Fill([(10, 1m), (12, 2m)]), db: db,
                cancellationToken: TestContext.Current.CancellationToken);
            await Report.Update(report.Id, r => r.Cover([2, 3]), db: db,
                cancellationToken: TestContext.Current.CancellationToken);
            var added = Report.Titled("New", [5], [(7, 0, false, [70])]);
            await added.Save(db: db, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(Writes());
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal([(10, 1m), (12, 2m)], await FuellingsAsync(Acme, note.Id));
        Assert.Equal([2, 3], await SitesAsync(Acme, report.Id));
        using (Tenant.Use(Acme))
        {
            Assert.Equal(2, await Report.Count(cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task A_fuelling_changed_without_its_note_is_refused()
    {
        var note = await IssueAsync(Acme, "A-1", (10, 12.5m));
        await using var context = await OpenAsync();

        InvalidOperationException refused;
        using (Tenant.Use(Acme))
        {
            var fuelling = await context.Set<DeliveryNoteTransaction>().SingleAsync(TestContext.Current.CancellationToken);
            fuelling.Measure(99m);
            refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        Assert.Contains("'DeliveryNoteTransaction' was changed on its own", refused.Message, StringComparison.Ordinal);
        Assert.Equal([(10, 12.5m)], await FuellingsAsync(Acme, note.Id));
    }

    [Fact]
    public async Task A_fuelling_added_without_its_note_is_refused()
    {
        await using var context = await OpenAsync();
        context.Add(DeliveryNoteTransaction.For(10, 1m));

        InvalidOperationException refused;
        using (Tenant.Use(Acme))
        {
            refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        Assert.Contains("'DeliveryNoteTransaction' was added on its own", refused.Message, StringComparison.Ordinal);
    }

    private static async Task<DeliveryNote> IssueAsync(int tenant, string number, params (int, decimal)[] rows)
    {
        using (Tenant.Use(tenant))
        {
            var note = DeliveryNote.Issue(number, rows);
            await note.Save(cancellationToken: TestContext.Current.CancellationToken);
            return note;
        }
    }

    private static async Task<Report> TitleAsync(
        int tenant, string title, int[] sites, (int ColumnId, int Order, bool Sum, int[] Totals)[] layout)
    {
        using (Tenant.Use(tenant))
        {
            var report = Report.Titled(title, sites, layout);
            await report.Save(cancellationToken: TestContext.Current.CancellationToken);
            return report;
        }
    }

    private async Task<List<(int, decimal)>> FuellingsAsync(int tenant, int noteId)
    {
        await using var context = await OpenAsync();
        using (Tenant.Use(tenant))
        {
            var rows = await context.Set<DeliveryNoteTransaction>().AsNoTracking()
                .Where(t => t.DeliveryNoteId == noteId)
                .ToListAsync(TestContext.Current.CancellationToken);
            return [.. rows.OrderBy(static t => t.TransactionId).Select(static t => (t.TransactionId, t.Quantity))];
        }
    }

    private async Task<List<int>> SitesAsync(int tenant, int reportId)
    {
        await using var context = await OpenAsync();
        using (Tenant.Use(tenant))
        {
            var rows = await context.Set<ReportSite>().AsNoTracking()
                .Where(s => s.ReportId == reportId)
                .ToListAsync(TestContext.Current.CancellationToken);
            return [.. rows.Select(static s => s.SiteId).Order()];
        }
    }

    private async Task<List<string>> LayoutAsync(int tenant, int reportId)
    {
        await using var context = await OpenAsync();
        using (Tenant.Use(tenant))
        {
            var columns = await context.Set<ReportColumn>().AsNoTracking().Include(c => c.Totals)
                .Where(c => c.ReportId == reportId)
                .ToListAsync(TestContext.Current.CancellationToken);
            return [.. columns.OrderBy(static c => c.ColumnId).Select(static c =>
                $"{c.ColumnId}:{c.Order}:{c.Sum}:{string.Join(',', c.Totals.Select(static t => t.TotalColumnId).Order())}")];
        }
    }

    // The INSERT, UPDATE and DELETE statements the saves since the last Clear sent, one per row.
    private List<string> Writes() =>
        [.. _log.Texts
            .SelectMany(static t => t.Split(';'))
            .Select(static s => s.Trim())
            .Where(static s => s.StartsWith("INSERT", StringComparison.Ordinal)
                               || s.StartsWith("UPDATE", StringComparison.Ordinal)
                               || s.StartsWith("DELETE", StringComparison.Ordinal))];

    private Task<FuelContext> OpenAsync() =>
        _app.GetRequiredService<IDbContextFactory<FuelContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);
}
