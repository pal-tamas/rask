using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// `rask audit`: every stored aggregate asked the rules its type declares today — the rules a save asks, every member
// of every row of it, plus each value object's own and the ids kept as text — and each refusal reported by rule and
// field with the ids alone. Rows are broken behind the rules' backs (ExecuteUpdate, raw SQL), as old data is.
[Collection(DataDbCollection.Name)]
public sealed class RuleAuditTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_stored_aggregate_every_rule_accepts_is_counted_and_reported_clean()
    {
        await using var database = await StartDatabaseAsync();
        await Opened("North", "ab12", 2).Save(cancellationToken: Token);

        var report = await RuleAudit.RunAsync(["Kiosk"], Token);

        Assert.True(report.Clean);
        Assert.Equal([new RuleAuditCount("Kiosk", 1)], report.Read);
    }

    [Fact]
    public async Task An_attribute_a_stored_row_breaks_is_reported_under_its_field_with_the_aggregates_id()
    {
        await using var database = await StartDatabaseAsync();
        var kiosk = Opened("North", "ab12", 2);
        await kiosk.Save(cancellationToken: Token);
        await database.Context.Set<Kiosk>().ExecuteUpdateAsync(s => s.SetProperty(k => k.Lanes, 20), Token);

        var report = await RuleAudit.RunAsync(["Kiosk"], Token);

        var breach = Assert.Single(report.Breaches);
        Assert.Equal(("Kiosk", "Lanes"), (breach.Aggregate, breach.Field));
        Assert.Equal([kiosk.Id.Value.ToString()], breach.Ids);
    }

    [Fact]
    public async Task A_value_object_tightened_since_is_asked_again_though_loading_never_asks_it()
    {
        await using var database = await StartDatabaseAsync();
        await Opened("North", "ab12", 2).Save(cancellationToken: Token);
        await SetColumn(database, "Name", "A name far longer than ten");

        var report = await RuleAudit.RunAsync(["Kiosk"], Token);

        Assert.Equal("Name", Assert.Single(report.Breaches).Field);
    }

    [Fact]
    public async Task A_value_stored_untrimmed_or_in_small_letters_is_reported_as_not_kept_by_its_rules()
    {
        await using var database = await StartDatabaseAsync();
        await Opened("North", "ab12", 2).Save(cancellationToken: Token);
        await SetColumn(database, "Code", " ab12 ");

        var report = await RuleAudit.RunAsync(["Kiosk"], Token);

        var breach = Assert.Single(report.Breaches);
        Assert.Equal(("Code", StoredValueChecks.NotAsKept), (breach.Field, breach.Rule));
    }

    [Fact]
    public async Task The_aggregates_Validate_and_a_childs_rule_are_asked_the_childs_rows_counted_together()
    {
        await using var database = await StartDatabaseAsync();
        var bare = Opened("Bare", "x1", 1);
        var tilled = Opened("Tilled", "x2", 1);
        tilled.Fit(2);
        await bare.Save(cancellationToken: Token);
        await tilled.Save(cancellationToken: Token);
        await UntillAsync(database, bare);
        await database.Context.Set<KioskTill>().ExecuteUpdateAsync(s => s.SetProperty(t => t.Number, 0), Token);

        var report = await RuleAudit.RunAsync(["Kiosk"], Token);

        Assert.Equal(["Tills", "Tills[*].Number"], report.Breaches.Select(b => b.Field));
        Assert.Equal([bare.Id.Value.ToString()], report.Breaches[0].Ids);
        Assert.Equal([tilled.Id.Value.ToString()], report.Breaches[1].Ids);
    }

    [Fact]
    public async Task An_awaited_rule_is_asked_of_every_stored_row()
    {
        await using var database = await StartDatabaseAsync();
        var booth = Booth.Seating(2);
        await booth.Save(cancellationToken: Token);
        await database.Context.Set<Booth>().ExecuteUpdateAsync(s => s.SetProperty(b => b.Seats, 6), Token);

        var report = await RuleAudit.RunAsync(["booth"], Token);

        var breach = Assert.Single(report.Breaches);
        Assert.Equal(("Booth", "Seats", "A booth seats four at most."), (breach.Aggregate, breach.Field, breach.Rule));
    }

    [Fact]
    public async Task An_id_kept_as_text_in_other_letters_or_no_id_at_all_is_reported_before_any_row_is_loaded()
    {
        await using var database = await StartDatabaseAsync();
        var upper = Opened("Upper", "u1", 1);
        var broken = Opened("Broken", "b1", 1);
        await upper.Save(cancellationToken: Token);
        await broken.Save(cancellationToken: Token);
        await UntillAsync(database, upper);
        await UntillAsync(database, broken);
        await SetColumn(database, "Id", upper.Id.Value.ToString().ToUpperInvariant(), where: upper.Id.Value.ToString());
        await SetColumn(database, "Id", "not-a-guid", where: broken.Id.Value.ToString());

        var report = await RuleAudit.RunAsync(["Kiosk"], Token);

        var ids = report.Breaches.Where(b => b.Field == "Id").ToDictionary(b => b.Rule, b => b.Ids);
        Assert.Equal([upper.Id.Value.ToString().ToUpperInvariant()], ids[StoredIdChecks.NotAsWritten]);
        Assert.Equal(["not-a-guid"], ids[StoredIdChecks.Unreadable]);
        Assert.Contains(report.Breaches, b => b.Rule.StartsWith("Rows could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_audit_writes_nothing_back_not_even_a_value_its_rules_would_keep_otherwise()
    {
        await using var database = await StartDatabaseAsync();
        await Opened("North", "ab12", 2).Save(cancellationToken: Token);
        await SetColumn(database, "Code", " ab12 ");

        await RuleAudit.RunAsync([], Token);

        Assert.Equal(" ab12 ", await ColumnOf(database, "Code"));
    }

    [Fact]
    public async Task A_name_that_is_no_aggregate_is_refused_with_the_names_that_are()
    {
        await using var database = await StartDatabaseAsync();

        var refused = await Assert.ThrowsAsync<ArgumentException>(() => RuleAudit.RunAsync(["Kiosks"], Token));

        Assert.Contains("'Kiosks'", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Kiosk,", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Naming_an_aggregate_asks_that_one_alone()
    {
        await using var database = await StartDatabaseAsync();
        await Booth.Seating(2).Save(cancellationToken: Token);
        await Opened("North", "ab12", 2).Save(cancellationToken: Token);
        await database.Context.Set<Booth>().ExecuteUpdateAsync(s => s.SetProperty(b => b.Seats, 6), Token);

        var report = await RuleAudit.RunAsync(["Kiosk"], Token);

        Assert.True(report.Clean);
        Assert.Equal(["Kiosk"], report.Read.Select(r => r.Aggregate));
    }

    // Its tills deleted behind its Validate()'s back.
    private static Task<int> UntillAsync(TestDatabase database, Kiosk kiosk)
    {
        var ids = kiosk.Tills.Select(t => t.Id).ToList();
        return database.Context.Set<KioskTill>().Where(t => ids.Contains(t.Id)).ExecuteDeleteAsync(Token);
    }

    private static Kiosk Opened(string name, string code, int lanes)
    {
        var kiosk = Kiosk.Open(name, code, lanes);
        kiosk.Fit(1);
        return kiosk;
    }

    // A column of the kiosks' table set behind every rule's back, as a row from before the rule was.
#pragma warning disable EF1002, EF1003 // the names are the model's own; the values go as parameters
    private static Task<int> SetColumn(TestDatabase database, string column, string value, string? where = null) =>
        database.Context.Database.ExecuteSqlRawAsync(
            $"UPDATE \"{Table(database)}\" SET \"{column}\" = {{0}}" + (where is null ? "" : " WHERE \"Id\" = {1}"),
            where is null ? [value] : [value, where],
            Token);

    private static Task<string> ColumnOf(TestDatabase database, string column) =>
        database.Context.Database.SqlQueryRaw<string>($"SELECT \"{column}\" AS \"Value\" FROM \"{Table(database)}\"").SingleAsync(Token);
#pragma warning restore EF1002, EF1003

    private static string Table(TestDatabase database) => database.Context.Model.FindEntityType(typeof(Kiosk))!.GetTableName()!;

    // A named in-memory database, kept alive by the connection the database holds until it is disposed. Not async: the
    // database binds the model surface to the flow that starts it.
    private static Task<TestDatabase> StartDatabaseAsync()
    {
        var keeper = new SqliteConnection($"Data Source=audit-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        keeper.Open();
        return TestDatabase.StartHolding(o => o.UseSqlite(keeper.ConnectionString), keeper, null, null, Token);
    }
}
