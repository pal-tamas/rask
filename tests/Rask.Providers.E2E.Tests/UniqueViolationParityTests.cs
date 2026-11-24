using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

/// <summary>
///     A duplicate in a unique index fails with the index's message, under its fields, on every provider — SQLite, which
///     names the columns rather than the index, included — so a test on SQLite sees the refusal production gives.
/// </summary>
public abstract class UniqueViolationParityTests(ProviderLeg leg) : ParityTests<SubscriberDbContext>(leg)
{
    [Fact]
    public async Task A_duplicate_in_a_single_column_index_fails_with_the_message_under_that_field()
    {
        RequireServer();
        await using var db = await SeededAsync();

        db.Add(new Subscriber { Email = "ada@example.com", Room = "A", Seat = 2, Nickname = "ada2" });
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => db.SaveChangesAsync(Ct));

        Assert.Equal([Subscriber.EmailTaken], refused.Errors["Email"]);
        Assert.Single(refused.Errors);
    }

    [Fact]
    public async Task A_duplicate_in_a_composite_index_fails_once_with_the_message_over_both_fields()
    {
        RequireServer();
        await using var db = await SeededAsync();

        db.Add(new Subscriber { Email = "bob@example.com", Room = "A", Seat = 1, Nickname = "bob" });
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => db.SaveChangesAsync(Ct));

        Assert.Equal(["Room", "Seat"], Assert.Single(refused.Failures).Fields);
        Assert.Equal([Subscriber.SeatTaken], refused.Errors["Room"]);
        Assert.Equal([Subscriber.SeatTaken], refused.Errors["Seat"]);
    }

    [Fact]
    public async Task An_index_with_no_message_keeps_the_providers_own_error()
    {
        RequireServer();
        await using var db = await SeededAsync();

        db.Add(new Subscriber { Email = "bob@example.com", Room = "A", Seat = 3, Nickname = "ada" });

        var failed = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        Assert.IsNotType<RaskValidationException>(failed.InnerException);
    }

    [Fact]
    public async Task An_execute_update_that_makes_a_duplicate_fails_with_the_message_too()
    {
        RequireServer();
        await using var db = await SeededAsync();
        db.Add(new Subscriber { Email = "bob@example.com", Room = "B", Seat = 1, Nickname = "bob" });
        await db.SaveChangesAsync(Ct);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => db.Subscribers
            .Where(s => s.Nickname == "bob")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, "ada@example.com"), Ct));

        Assert.Equal([Subscriber.EmailTaken], refused.Errors["Email"]);
    }

    private protected override SubscriberDbContext Open() => Leg.Open<SubscriberDbContext>(o => new SubscriberDbContext(o));

    private async Task<SubscriberDbContext> SeededAsync()
    {
        var db = Open();
        db.Add(new Subscriber { Email = "ada@example.com", Room = "A", Seat = 1, Nickname = "ada" });
        await db.SaveChangesAsync(Ct);
        return db;
    }
}

public sealed class SqliteUniqueViolationParityTests() : UniqueViolationParityTests(ProviderLeg.Sqlite("unique-violations"));

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerUniqueViolationParityTests() : UniqueViolationParityTests(ProviderLeg.SqlServer("rask_e2e_unique_violations"));

[Collection(PostgresCollection.Name)]
public sealed class PostgresUniqueViolationParityTests() : UniqueViolationParityTests(ProviderLeg.Postgres(SubscriberDbContext.Schema));
