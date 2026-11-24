using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
///     F-25, F-24, F-69: the writes over many rows of one aggregate type — <c>UpdateWhere</c>/<c>DeleteWhere</c> as one
///     statement that still keeps the stamps, the locks and the one-aggregate rule; <c>UpdateEach</c>, <c>Upsert</c> and
///     <c>Import</c> through the aggregate itself, its rules asked.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class BulkWritesTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-bulk-writes-{Guid.NewGuid():N}.db");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task DeleteWhere_deletes_the_rows_it_matches_and_counts_them()
    {
        await using var database = await StartAsync();
        await FirkinsAsync(("A", "a1", 1), ("A", "a2", 2), ("B", "b1", 3));

        var deleted = await Firkin.DeleteWhere(c => c.Shelf == "A", Token);

        Assert.Equal(2, deleted);
        Assert.Equal(["b1"], await LabelsAsync(database));
    }

    [Fact]
    public async Task DeleteWhere_on_a_soft_deleted_aggregate_stamps_the_rows_and_keeps_them()
    {
        await using var database = await StartAsync();
        await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: Token);
        await GeneratedModelWrites.Create(Widget.Create("hammer"), cancellationToken: Token);

        await Widget.DeleteWhere(w => w.Name == "anvil", Token);

        Assert.Equal(1, await Widget.Count(Token));
        Assert.Equal(2, await database.Context.Set<Widget>().IgnoreQueryFilters().CountAsync(Token));
    }

    [Fact]
    public async Task DeleteWhere_refuses_an_aggregate_whose_ValidateDelete_asks_each_row()
    {
        await using var database = await StartAsync();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Voyage.DeleteWhere(v => v.Seats > 0, Token));

        Assert.Contains("ValidateDelete()", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Voyage.Delete(id)", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteWhere_refuses_rows_of_a_locked_parent_and_deletes_nothing()
    {
        await using var database = await StartAsync();
        var closed = await PeriodAsync(database, "March", close: false);
        var open = await PeriodAsync(database, "April", close: false);
        await GeneratedModelWrites.Create(FirkinSlip.Into(closed.Id, "fuel"), cancellationToken: Token);
        await GeneratedModelWrites.Create(FirkinSlip.Into(open.Id, "oil"), cancellationToken: Token);
        closed.Close();
        await database.Context.SaveChangesAsync(Token);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => FirkinSlip.DeleteWhere(s => s.Text != "", Token));

        Assert.Equal(TallyNote.PeriodClosed, Assert.Single(refused.Failures).Message);
        Assert.Equal(2, await database.Context.Set<FirkinSlip>().CountAsync(Token));
    }

    [Fact]
    public async Task UpdateWhere_sets_the_columns_and_stamps_UpdatedAt_and_Version_as_a_save_would()
    {
        await using var database = await StartAsync();
        var firkin = await GeneratedModelWrites.Create(Firkin.On("A", "a1", 1), cancellationToken: Token);
        var before = (await database.Load<Firkin>(firkin.Id, Token))!;

        var changed = await Firkin.UpdateWhere(c => c.Shelf == "A", s => s.SetProperty(c => c.Units, 9), Token);

        var after = (await database.Load<Firkin>(firkin.Id, Token))!;
        Assert.Equal(1, changed);
        Assert.Equal((9, before.Version + 1), (after.Units, after.Version));
        Assert.True(after.UpdatedAt >= before.UpdatedAt);
    }

    [Fact]
    public async Task UpdateWhere_refuses_a_row_moved_into_a_locked_parent_and_rolls_the_statement_back()
    {
        await using var database = await StartAsync();
        var closed = await PeriodAsync(database, "March", close: true);
        var open = await PeriodAsync(database, "April", close: false);
        var note = TallyNote.Into(open.Id, "Fuel");
        database.Context.Add(note);
        await database.Context.SaveChangesAsync(Token);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() =>
            TallyNote.UpdateWhere(n => n.Id == note.Id, s => s.SetProperty(n => n.Period, closed.Id), Token));

        Assert.Equal(["Period"], Assert.Single(refused.Failures).Fields);
        Assert.Equal(open.Id, (await database.Load<TallyNote>(note.Id, Token))!.Period);
    }

    [Fact]
    public async Task A_set_write_counts_as_one_aggregate_so_a_second_one_beside_it_is_refused()
    {
        await using var database = await StartAsync();
        await FirkinsAsync(("A", "a1", 1));

        var refused = await Assert.ThrowsAsync<ManyAggregatesException>(() => Db.Transaction(async () =>
        {
            await Firkin.UpdateWhere(c => c.Shelf == "A", s => s.SetProperty(c => c.Units, 2), Token);
            await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: Token);
        }, cancellationToken: Token));

        Assert.Contains("Firkin (set)", refused.Aggregates);
        Assert.Equal(1, (await database.Context.Set<Firkin>().AsNoTracking().SingleAsync(Token)).Units);
    }

    [Fact]
    public async Task UpdateEach_runs_the_method_on_each_aggregate_and_writes_it_through_its_rules()
    {
        await using var database = await StartAsync();
        await FirkinsAsync(("A", "a1", 1), ("A", "a2", 2), ("B", "b1", 3));

        var updated = await Firkin.UpdateEach(c => c.Shelf == "A", c => c.Restock(c.Units * 10), batchSize: 1, Token);

        Assert.Equal(2, updated);
        Assert.Equal([10, 20, 3], await database.Context.Set<Firkin>().AsNoTracking().OrderBy(c => c.Id).Select(c => c.Units).ToListAsync(Token));
    }

    [Fact]
    public async Task UpdateEach_rolls_back_only_the_refused_batch_and_keeps_the_ones_before_it()
    {
        await using var database = await StartAsync();
        await FirkinsAsync(("A", "a1", 1), ("A", "a2", 2), ("A", "a3", 3));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() =>
            Firkin.UpdateEach(c => c.Shelf == "A", c => c.Restock(c.Label == "a2" ? -1 : c.Units + 1), batchSize: 1, Token));

        Assert.Equal(Firkin.Negative, Assert.Single(refused.Failures).Message);
        Assert.Equal([2, 2, 3], await database.Context.Set<Firkin>().AsNoTracking().OrderBy(c => c.Id).Select(c => c.Units).ToListAsync(Token));
    }

    [Fact]
    public async Task UpdateEach_is_one_batch_of_its_own_aggregates_which_the_one_aggregate_rule_lets_through()
    {
        await using var database = await StartAsync();
        await FirkinsAsync(("A", "a1", 1), ("A", "a2", 2));
        using var saves = Db.Record();

        await Firkin.UpdateEach(c => c.Shelf == "A", c => c.MoveTo("B"), cancellationToken: Token);

        var batch = Assert.Single(saves.Transactions);
        Assert.Equal(2, batch.Aggregates.Count);
        Assert.StartsWith("Firkin.UpdateEach", batch.ManyAggregates, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upsert_makes_one_when_none_matches_and_changes_the_one_that_does()
    {
        await using var database = await StartAsync();

        var made = await Firkin.Upsert(c => c.Label == "a1", () => Firkin.On("A", "a1", 1), c => c.Restock(5), Token);
        var changed = await Firkin.Upsert(c => c.Label == "a1", () => Firkin.On("A", "a1", 1), c => c.Restock(5), Token);

        Assert.Equal((true, false), (made.Created, changed.Created));
        Assert.Equal(5, (await database.Context.Set<Firkin>().AsNoTracking().SingleAsync(Token)).Units);
    }

    [Fact]
    public async Task Upsert_refuses_a_match_of_more_than_one()
    {
        await using var database = await StartAsync();
        await FirkinsAsync(("A", "a1", 1), ("A", "a2", 2));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Firkin.Upsert(c => c.Shelf == "A", () => Firkin.On("A", "a3", 1), c => c.Restock(5), Token));

        Assert.Contains("matched more than one Firkin", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_saves_every_row_through_the_factory_in_batches()
    {
        await using var database = await StartAsync();
        var lines = Enumerable.Range(1, 7).Select(static i => new FirkinLine("A", $"a{i}", i)).ToList();

        var result = await Firkin.Import(lines, static l => Firkin.On(l.Shelf, l.Label, l.Count), static o => o.BatchSize = 3, Token);

        Assert.Equal((7, 0), (result.Inserted, result.Updated));
        Assert.Equal(7, await database.Context.Set<Firkin>().CountAsync(Token));
    }

    [Fact]
    public async Task Import_collects_the_rows_its_rules_refuse_with_their_position_and_saves_the_rest()
    {
        await using var database = await StartAsync();
        FirkinLine[] lines = [new("A", "a1", 1), new("A", "a2", -1), new("A", "a3", 3)];

        var result = await Firkin.Import(lines, static l => Firkin.On(l.Shelf, l.Label, l.Count), static o => o.OnInvalid = ImportFailure.Collect, Token);

        var failure = Assert.Single(result.Failures);
        Assert.Equal((1, "a2"), (failure.Index, failure.Row.Label));
        Assert.Equal(Firkin.Negative, Assert.Single(failure.Refusal.Failures).Message);
        Assert.Equal(["a1", "a3"], await LabelsAsync(database));
    }

    [Fact]
    public async Task Import_with_a_natural_key_changes_what_is_stored_and_what_an_earlier_row_made()
    {
        await using var database = await StartAsync();
        await FirkinsAsync(("A", "a1", 1));
        FirkinLine[] lines = [new("B", "a1", 5), new("B", "a2", 2), new("B", "a2", 7)];

        var result = await Firkin.Import(
            lines,
            static l => Firkin.On(l.Shelf, l.Label, l.Count),
            static o => o.Upsert(c => c.Label, static (c, l) => c.Restock(l.Count)),
            Token);

        Assert.Equal((1, 2), (result.Inserted, result.Updated));
        Assert.Equal([5, 7], await database.Context.Set<Firkin>().AsNoTracking().OrderBy(c => c.Label).Select(c => c.Units).ToListAsync(Token));
    }

    [Fact]
    public async Task Import_that_collects_its_refusals_is_refused_inside_a_transaction()
    {
        await using var database = await StartAsync();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(() =>
            Firkin.Import([new FirkinLine("A", "a1", 1)], static l => Firkin.On(l.Shelf, l.Label, l.Count),
                static o => o.OnInvalid = ImportFailure.Collect, Token), cancellationToken: Token));

        Assert.Contains("ImportFailure.Collect", refused.Message, StringComparison.Ordinal);
    }

    private static async Task FirkinsAsync(params (string Shelf, string Label, int Units)[] firkins)
    {
        foreach (var (shelf, label, units) in firkins)
        {
            await GeneratedModelWrites.Create(Firkin.On(shelf, label, units), cancellationToken: Token);
        }
    }

    private static Task<List<string>> LabelsAsync(TestDatabase database) =>
        database.Context.Set<Firkin>().AsNoTracking().OrderBy(c => c.Label).Select(c => c.Label).ToListAsync(Token);

    private static async Task<TallyPeriod> PeriodAsync(TestDatabase database, string name, bool close)
    {
        var period = TallyPeriod.Open(name);
        if (close)
        {
            period.Close();
        }

        database.Context.Add(period);
        await database.Context.SaveChangesAsync(Token);
        return period;
    }

    private Task<TestDatabase> StartAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"), cancellationToken: Token);
}
