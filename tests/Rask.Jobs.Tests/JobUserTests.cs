using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Jobs.Tests;

/// <summary>
/// A job runs for the user who enqueued it: the row records <see cref="Current.UserId" />, and the processor
/// re-enters it before the handler runs — later, on another thread, with nobody signed in.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobUserTests
{
    [Fact]
    public async Task A_handler_runs_as_the_user_who_enqueued_it_and_an_anonymous_job_as_nobody()
    {
        var alice = Guid.NewGuid();
        await using var h = new JobsHarness();

        using (Current.UseUser(alice))
        {
            await h.Queue.Enqueue(new WhoAmIJob("alice"));
        }
        await h.Queue.Enqueue(new WhoAmIJob("anon"));

        // Started under somebody else, to prove the processor does not leak its own flow's user into a job
        // that recorded nobody.
        using (Current.UseUser(Guid.NewGuid()))
        {
            await h.RunUntilAsync(() => h.Recorder.Values.Count == 2);
        }

        Assert.Contains($"alice:{alice}", h.Recorder.Values);
        Assert.Contains("anon:nobody", h.Recorder.Values);
    }

    [Fact]
    public async Task A_database_without_the_user_column_says_which_commands_to_run()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rask-nouser-{Guid.NewGuid():N}.db");
        await using var h = new JobsHarness(dbPath: path);
        // The table as it looked before this change: everything but UserId.
        await using (var db = h.NewContext())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Job DROP COLUMN UserId;");

            // A pending row, written the way the old package wrote it. An empty queue never loads a row — the
            // claim selects ids only — so it is loading one that fails, every poll, swallowed.
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO Job (Type, Payload, RunAt, Attempts, CreatedAt, UpdatedAt)
                VALUES ('x', 'null', '2020-01-01 00:00:00', 0, '2020-01-01 00:00:00', '2020-01-01 00:00:00');
                """);
        }

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.WaitUntilAsync(() => Task.FromResult(
                h.Logs.Any(l => l.Contains("rask db add AddJobUser", StringComparison.Ordinal))));
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        Assert.DoesNotContain(
            h.Logs,
            l => l.Contains("Job processing cycle failed", StringComparison.Ordinal));
    }
}
