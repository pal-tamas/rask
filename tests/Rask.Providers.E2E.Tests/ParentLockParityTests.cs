using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Providers.E2E.Tests;

/// <summary>A month of the ledger: once closed, what was written into it stays.</summary>
public sealed class LedgerMonth : Aggregate<int>
{
    private LedgerMonth() { } // EF materialization

    public string Name { get; private set; } = "";

    public bool Closed { get; private set; }

    public static LedgerMonth Open(string name) => new() { Name = name };
}

/// <summary>A claim filed into a month, whose month is read under an update lock as the claim is saved.</summary>
public sealed class LedgerClaim : Aggregate<Guid>
{
    private LedgerClaim() { } // EF materialization

    public int Month { get; private set; }

    public static LedgerClaim Into(int month) => new() { Id = Guid.CreateVersion7(), Month = month };

    internal static void Lock(EntityTypeBuilder<LedgerClaim> builder) =>
        builder.LockedBy(c => c.Month, (LedgerMonth m) => m.Closed, m => $"{m.Name} is closed.", LockRead.ForUpdate);
}

public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_parent_lock";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Database.IsSqlite() ? null : Schema);
        modelBuilder.Entity<LedgerMonth>();
        modelBuilder.Entity<LedgerClaim>(LedgerClaim.Lock);
        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>
///     <c>LockedBy(…, LockRead.ForUpdate)</c> on every provider: the read of the parent is rewritten in the database's own
///     words (<c>UPDLOCK, HOLDLOCK</c>, <c>FOR UPDATE OF</c>) and runs, a save into a closed parent is refused in the same
///     sentence, and a close of the parent waits for a save that read it to commit — on SQLite because its one writer
///     serializes them, on a server because of the lock.
/// </summary>
public abstract class ParentLockParityTests(ProviderLeg leg) : ParityTests<LedgerDbContext>(leg)
{
    [Fact]
    public async Task A_claim_into_an_open_month_saves_through_the_locked_read()
    {
        RequireServer();
        var month = await MonthAsync("April", close: false);
        await using var db = Open();

        db.Add(LedgerClaim.Into(month));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(1, await db.Set<LedgerClaim>().CountAsync(Ct));
    }

    [Fact]
    public async Task A_claim_into_a_closed_month_is_refused_with_the_months_sentence()
    {
        RequireServer();
        var month = await MonthAsync("March", close: true);
        await using var db = Open();

        db.Add(LedgerClaim.Into(month));
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => db.SaveChangesAsync(Ct));

        Assert.Equal("March is closed.", Assert.Single(refused.Errors["Month"]));
        Assert.Equal(0, await db.Set<LedgerClaim>().CountAsync(Ct));
    }

    [Fact]
    public async Task Closing_the_month_waits_for_a_claim_that_read_it_to_commit()
    {
        RequireServer();
        var month = await MonthAsync("May", close: false);
        await using var saving = Open();
        await using var closing = Open();

        // In the strategy's own unit, as a retrying provider requires of a transaction the caller opens. The close runs
        // on a thread of its own: SQLite's provider waits for a lock synchronously, inside the call.
        var waited = await saving.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await saving.Database.BeginTransactionAsync(Ct);
            saving.Add(LedgerClaim.Into(month));
            await saving.SaveChangesAsync(Ct);

            var close = Task.Run(() => closing.Set<LedgerMonth>().Where(m => m.Id == month)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Closed, true), Ct), Ct);
            var held = await Task.WhenAny(close, Task.Delay(TimeSpan.FromMilliseconds(500), Ct)) != close;
            await transaction.CommitAsync(Ct);
            await close;
            return held;
        });

        Assert.True(waited);
        Assert.True(await closing.Set<LedgerMonth>().Where(m => m.Id == month).Select(m => m.Closed).SingleAsync(Ct));
        Assert.Equal(1, await closing.Set<LedgerClaim>().CountAsync(Ct));
    }

    private protected override LedgerDbContext Open() => Leg.Open<LedgerDbContext>(o => new LedgerDbContext(o));

    private async Task<int> MonthAsync(string name, bool close)
    {
        await using var db = Open();
        var month = LedgerMonth.Open(name);
        db.Add(month);
        await db.SaveChangesAsync(Ct);
        if (close)
        {
            await db.Set<LedgerMonth>().Where(m => m.Id == month.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.Closed, true), Ct);
        }

        return month.Id;
    }
}

public sealed class SqliteParentLockParityTests() : ParentLockParityTests(ProviderLeg.Sqlite("parent-lock"));

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerParentLockParityTests() : ParentLockParityTests(ProviderLeg.SqlServer("rask_e2e_parent_lock"));

[Collection(PostgresCollection.Name)]
public sealed class PostgresParentLockParityTests() : ParentLockParityTests(ProviderLeg.Postgres(LedgerDbContext.Schema));
