using Microsoft.EntityFrameworkCore;

namespace Rask.Jobs.Tests;

/// <summary>
/// Upgrading the package without running the migration is the one failure mode of the lease change that
/// would otherwise be silent: the exception is swallowed by the cycle's catch, so the app looks healthy
/// while logging the same error every poll and never processing a job again.
/// </summary>
public sealed class MissingLeaseColumnTests
{
    [Fact]
    public async Task A_database_without_the_lease_columns_says_which_commands_to_run()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rask-nolease-{Guid.NewGuid():N}.db");
        await using var h = new JobsHarness(dbPath: path);

        // Recreate the Jobs table as it looked before this change — no ClaimToken, no ClaimedUntil.
        await using (var db = h.NewContext())
        {
            await db.Database.ExecuteSqlRawAsync("DROP TABLE Job;");
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE Job (
                    Id INTEGER NOT NULL CONSTRAINT PK_Job PRIMARY KEY AUTOINCREMENT,
                    Type TEXT NOT NULL,
                    Payload TEXT NOT NULL,
                    RunAt TEXT NOT NULL,
                    ProcessedAt TEXT NULL,
                    Attempts INTEGER NOT NULL,
                    Error TEXT NULL,
                    CreatedAt TEXT NOT NULL);
                """);
        }

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.WaitUntilAsync(() => Task.FromResult(
                h.Logs.Any(l => l.Contains("rask db add AddJobLeases", StringComparison.Ordinal))));
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        // Repeated once per poll, which is exactly the problem being made visible — take the first.
        var message = h.Logs.First(l => l.Contains("rask db add AddJobLeases", StringComparison.Ordinal));
        Assert.Contains("lease columns", message, StringComparison.Ordinal);
        Assert.Contains("rask db update", message, StringComparison.Ordinal);

        // The generic "cycle failed" text would send someone reading a stack trace instead.
        Assert.DoesNotContain(
            h.Logs,
            l => l.Contains("Job processing cycle failed", StringComparison.Ordinal));
    }
}
