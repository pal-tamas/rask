using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data.Tests;

// b.LockedBy(n => n.Period, (TallyPeriod p) => p.Closed, …): every save of a note — and of a line of one — asks the
// store whether the period the note was read in, or the one it is written into, is closed.
public sealed class LockedByTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();
    // One instance for every test: a new interceptor per context is a new EF Core service provider each time.
    private static readonly LockReads Reads = new();
    private static readonly ClaimInsertFault Fault = new();

    public void Dispose() => _sqlite.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_note_written_into_a_closed_period_is_refused_under_its_period_field()
    {
        await using var database = await StartDatabaseAsync();
        var closed = await PeriodAsync(database, "March", close: true);

        database.Context.Add(TallyNote.Into(closed.Id, "Fuel"));
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => database.Context.SaveChangesAsync(Token));

        Assert.Equal(TallyNote.PeriodClosed, Assert.Single(refused.Errors["Period"]));
        Assert.Empty(await database.Context.Set<TallyNote>().AsNoTracking().ToListAsync(Token));
    }

    [Fact]
    public async Task A_note_written_into_an_open_period_saves()
    {
        await using var database = await StartDatabaseAsync();
        var open = await PeriodAsync(database, "April", close: false);

        database.Context.Add(TallyNote.Into(open.Id, "Fuel"));
        await database.Context.SaveChangesAsync(Token);

        Assert.Single(await database.Context.Set<TallyNote>().AsNoTracking().ToListAsync(Token));
    }

    [Fact]
    public async Task A_note_moved_out_of_a_closed_period_is_refused_as_the_old_period_is_locked_too()
    {
        await using var database = await StartDatabaseAsync();
        var march = await PeriodAsync(database, "March", close: false);
        var april = await PeriodAsync(database, "April", close: false);
        var note = await NoteAsync(database, march.Id);
        march.Close();
        await database.Context.SaveChangesAsync(Token);

        note.MoveTo(april.Id);
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => database.Context.SaveChangesAsync(Token));

        Assert.Equal(TallyNote.PeriodClosed, Assert.Single(refused.Errors["Period"]));
    }

    [Fact]
    public async Task A_note_moved_between_two_open_periods_saves()
    {
        await using var database = await StartDatabaseAsync();
        var march = await PeriodAsync(database, "March", close: false);
        var april = await PeriodAsync(database, "April", close: false);
        var note = await NoteAsync(database, march.Id);

        note.MoveTo(april.Id);
        await database.Context.SaveChangesAsync(Token);

        Assert.Equal(april.Id, (await database.Load<TallyNote>(note.Id, Token))!.Period);
    }

    [Fact]
    public async Task A_closed_periods_note_reworded_is_refused_about_the_whole_note()
    {
        await using var database = await StartDatabaseAsync();
        var march = await PeriodAsync(database, "March", close: false);
        var note = await NoteAsync(database, march.Id);
        march.Close();
        await database.Context.SaveChangesAsync(Token);

        note.Reword("Changed after closing");
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => database.Context.SaveChangesAsync(Token));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(TallyNote.PeriodClosed, failure.Message);
        Assert.Empty(failure.Fields);
    }

    [Fact]
    public async Task A_line_changed_added_or_removed_on_a_closed_periods_note_is_refused_through_its_note()
    {
        await using var database = await StartDatabaseAsync();
        var march = await PeriodAsync(database, "March", close: false);
        var note = TallyNote.Into(march.Id, "Fuel");
        var line = note.Add(10);
        database.Context.Add(note);
        await database.Context.SaveChangesAsync(Token);
        march.Close();
        await database.Context.SaveChangesAsync(Token);

        line.Change(20);
        var changed = await Assert.ThrowsAsync<RaskValidationException>(() => database.Context.SaveChangesAsync(Token));
        line.Change(10);
        note.Add(5);
        var added = await Assert.ThrowsAsync<RaskValidationException>(() => database.Context.SaveChangesAsync(Token));
        note.Remove(note.Lines[^1]);
        note.Remove(line);
        var removed = await Assert.ThrowsAsync<RaskValidationException>(() => database.Context.SaveChangesAsync(Token));

        Assert.All([changed, added, removed], refused => Assert.Equal(TallyNote.PeriodClosed, Assert.Single(refused.Failures).Message));
        Assert.Equal(10, Assert.Single((await database.Load<TallyNote>(note.Id, Token))!.Lines).Amount);
    }

    [Fact]
    public async Task A_closed_periods_note_deleted_is_refused_and_stays()
    {
        await using var database = await StartDatabaseAsync();
        var march = await PeriodAsync(database, "March", close: false);
        var note = await NoteAsync(database, march.Id);
        march.Close();
        await database.Context.SaveChangesAsync(Token);

        database.Context.Remove(note);
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => database.Context.SaveChangesAsync(Token));

        Assert.Equal(TallyNote.PeriodClosed, Assert.Single(refused.Failures).Message);
        Assert.NotNull(await database.Load<TallyNote>(note.Id, Token));
    }

    [Fact]
    public async Task A_parent_key_left_empty_locks_nothing_and_one_set_from_empty_to_a_closed_parent_is_refused()
    {
        await using var database = await StartDatabaseAsync();
        var closed = await PeriodAsync(database, "March", close: true);
        var claim = TallyClaim.Unfiled();
        database.Context.Add(claim);
        await database.Context.SaveChangesAsync(Token);

        claim.FileIn(closed.Id);
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => database.Context.SaveChangesAsync(Token));

        Assert.Equal("March is closed.", Assert.Single(refused.Errors["Period"]));
    }

    [Fact]
    public async Task A_lock_read_for_update_runs_inside_the_saves_transaction_and_the_save_commits()
    {
        await using var database = await StartDatabaseAsync();
        var open = await PeriodAsync(database, "April", close: false);
        var claim = TallyClaim.Unfiled();
        claim.FileIn(open.Id);
        Reads.Seen.Clear();

        database.Context.Add(claim);
        await database.Context.SaveChangesAsync(Token);

        var read = Assert.Single(Reads.Seen, r => r.Sql.StartsWith("-- " + ForUpdateSql.Tag, StringComparison.Ordinal));
        Assert.True(read.InTransaction);
        Assert.Null(database.Context.Database.CurrentTransaction);
        Assert.Equal(open.Id, (await database.Load<TallyClaim>(claim.Id, Token))!.Period);
    }

    [Fact]
    public async Task A_lock_read_for_update_under_a_retrying_strategy_runs_inside_the_saves_transaction()
    {
        await using var database = await StartRetryingDatabaseAsync();
        var open = await PeriodAsync(database, "April", close: false);
        var claim = TallyClaim.Unfiled();
        claim.FileIn(open.Id);
        Reads.Seen.Clear();

        await GeneratedModelWrites.Save(claim, cancellationToken: Token);

        var read = Assert.Single(Reads.Seen, r => r.Sql.StartsWith("-- " + ForUpdateSql.Tag, StringComparison.Ordinal));
        Assert.True(read.InTransaction);
        Assert.Equal(open.Id, (await database.Load<TallyClaim>(claim.Id, Token))!.Period);
    }

    [Fact]
    public async Task A_transient_failure_under_a_retrying_strategy_retries_the_lock_read_and_the_write_together()
    {
        await using var database = await StartRetryingDatabaseAsync();
        var open = await PeriodAsync(database, "May", close: false);
        var claim = TallyClaim.Unfiled();
        claim.FileIn(open.Id);
        Reads.Seen.Clear();
        Fault.FailNextInsert = true;

        await GeneratedModelWrites.Save(claim, cancellationToken: Token);

        var reads = Reads.Seen.Where(r => r.Sql.StartsWith("-- " + ForUpdateSql.Tag, StringComparison.Ordinal)).ToList();
        Assert.Equal(2, reads.Count);
        Assert.All(reads, r => Assert.True(r.InTransaction));
        Assert.Equal(open.Id, Assert.Single(await database.Context.Set<TallyClaim>().AsNoTracking().ToListAsync(Token)).Period);
    }

    [Fact]
    public async Task A_refused_save_rolls_back_the_transaction_its_lock_opened()
    {
        await using var database = await StartDatabaseAsync();
        var closed = await PeriodAsync(database, "March", close: true);
        var claim = TallyClaim.Unfiled();
        claim.FileIn(closed.Id);

        database.Context.Add(claim);
        await Assert.ThrowsAsync<RaskValidationException>(() => database.Context.SaveChangesAsync(Token));

        Assert.Null(database.Context.Database.CurrentTransaction);
        Assert.Empty(await database.Context.Set<TallyClaim>().AsNoTracking().ToListAsync(Token));
    }

    [Fact]
    public async Task A_change_to_only_the_excepted_claim_columns_passes_under_a_filed_refund()
    {
        await using var database = await StartDatabaseAsync();
        var (filed, transaction) = await FiledAsync(database);
        var other = TallyRefund.Named("Foreign 2026");
        database.Context.Add(other);
        await database.Context.SaveChangesAsync(Token);

        transaction.ClaimForeign(other.Id);
        transaction.ClaimFor(null);
        await database.Context.SaveChangesAsync(Token);

        var stored = (await database.Load<TallyTransaction>(transaction.Id, Token))!;
        Assert.Equal((null, other.Id), (stored.RefundId, stored.ForeignRefundId));
        Assert.NotEqual(Guid.Empty, filed.Id);
    }

    [Fact]
    public async Task Any_other_change_under_a_filed_refund_is_refused_with_the_change_sentence_from_its_filing()
    {
        await using var database = await StartDatabaseAsync();
        var (_, transaction) = await FiledAsync(database);

        transaction.Measure(40);
        transaction.ClaimForeign(Guid.CreateVersion7());
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => database.Context.SaveChangesAsync(Token));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal("Gas 2026, filed at Budapest, holds these 40 litres as filed.", failure.Message);
        Assert.Empty(failure.Fields);
    }

    [Fact]
    public async Task A_delete_under_a_filed_refund_is_refused_with_the_delete_sentence()
    {
        await using var database = await StartDatabaseAsync();
        var (_, transaction) = await FiledAsync(database);

        database.Context.Remove(transaction);
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => database.Context.SaveChangesAsync(Token));

        Assert.Equal("Gas 2026, filed at Budapest, keeps its transactions.", Assert.Single(refused.Failures).Message);
        Assert.NotNull(await database.Load<TallyTransaction>(transaction.Id, Token));
    }

    // A transaction claimed by a refund that is then filed.
    private static async Task<(TallyRefund Refund, TallyTransaction Transaction)> FiledAsync(TestDatabase database)
    {
        var refund = TallyRefund.Named("Gas 2026");
        var transaction = TallyTransaction.Of(30);
        transaction.ClaimFor(refund.Id);
        database.Context.AddRange(refund, transaction);
        await database.Context.SaveChangesAsync(Token);
        refund.FileAt("Budapest");
        await database.Context.SaveChangesAsync(Token);
        return (refund, transaction);
    }

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

    private static async Task<TallyNote> NoteAsync(TestDatabase database, TallyPeriodId period)
    {
        var note = TallyNote.Into(period, "Fuel");
        database.Context.Add(note);
        await database.Context.SaveChangesAsync(Token);
        return note;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString).AddInterceptors(Reads));

    // As Rask.SqlServer and Rask.Postgres configure a context by default: an execution strategy that retries.
    private Task<TestDatabase> StartRetryingDatabaseAsync() =>
        TestDatabase.Start(o => o
            .UseSqlite(_sqlite.ConnectionString, sqlite => sqlite.ExecutionStrategy(static d => new RetryingStrategy(d)))
            .AddInterceptors(Reads, Fault));

    // Retries the one failure a test throws, at once.
    private sealed class RetryingStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 2, maxRetryDelay: TimeSpan.FromMilliseconds(1))
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TransientFault;
    }

    private sealed class TransientFault() : Exception("A transient failure the strategy retries.");

    // Fails the next insert of a claim once, as a dropped connection would.
    private sealed class ClaimInsertFault : DbCommandInterceptor
    {
        public bool FailNextInsert { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Fail(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Fail(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Fail(DbCommand command)
        {
            if (FailNextInsert && command.CommandText.Contains("INSERT INTO \"TallyClaim", StringComparison.Ordinal))
            {
                FailNextInsert = false;
                throw new TransientFault();
            }
        }
    }

    // Each read the contexts run: its SQL as executed, and whether it ran inside a transaction.
    private sealed class LockReads : DbCommandInterceptor
    {
        public List<(string Sql, bool InTransaction)> Seen { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            lock (Seen)
            {
                Seen.Add((command.CommandText, command.Transaction is not null));
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
