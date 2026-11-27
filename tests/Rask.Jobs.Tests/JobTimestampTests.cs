using Microsoft.EntityFrameworkCore;
using Rask.Values;

namespace Rask.Background.Tests;

/// <summary>
///     A job's times are Rask's own <see cref="Timestamp" />s, each column the UTC instant it holds — on a plain context
///     too, which runs no Rask convention — so the two <c>02:30</c>s of an October night in Budapest stay apart.
/// </summary>
public sealed class JobTimestampTests
{
    private static readonly TimeZoneInfo Budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_jobs_times_in_the_repeated_autumn_hour_come_back_as_the_same_instants()
    {
        using var zone = AmbientClock.UseZone(Budapest);
        await using var h = new JobsHarness();
        var first = Timestamp.At(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero));
        var second = Timestamp.At(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero));
        var job = Job.For("Some.Job", "{}", first);
        job.Completed(second);
        await using (var db = h.NewContext())
        {
            db.Set<Job>().Add(job);
            await db.SaveChangesAsync(Ct);
        }

        await using var read = h.NewContext();
        var row = await read.Set<Job>().AsNoTracking().SingleAsync(Ct);
        var table = read.Model.FindEntityType(typeof(Job))!.GetTableName();
#pragma warning disable EF1003 // the table name is the model's own
        var stored = await read.Database.SqlQueryRaw<string>("SELECT \"RunAt\" AS \"Value\" FROM \"" + table + "\"").SingleAsync(Ct);
#pragma warning restore EF1003

        Assert.Equal(first.Value, second.Value);
        Assert.Equal(first.Instant, row.RunAt.Instant);
        Assert.Equal(second.Instant, row.ProcessedAt!.Instant);
        Assert.StartsWith("2026-10-25 00:30:00", stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_job_due_in_the_second_repeated_hour_is_not_claimed_in_the_first()
    {
        using var zone = AmbientClock.UseZone(Budapest);
        await using var h = new JobsHarness(start: new DateTimeOffset(2026, 10, 25, 0, 45, 0, TimeSpan.Zero));
        var due = Timestamp.At(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero));
        await using var db = h.NewContext();
        db.Set<Job>().Add(Job.For("Some.Job", "{}", due));
        await db.SaveChangesAsync(Ct);

        var claimed = await h.Jobs.ClaimAsync(db, h.Clock.GetUtcNow().UtcDateTime, Ct);

        Assert.Empty(claimed);
    }
}
