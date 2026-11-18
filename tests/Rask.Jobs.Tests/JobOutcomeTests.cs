using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.Background.Tests;

/// <summary>
///     How a handler says its job ended (#1368): <c>Jobs.Warn</c> ends it warned, <c>Jobs.Refuse</c> failed for good — never
///     retried, no dead letter, its words shown — and <c>Jobs.Note</c> adds words to a job that is done; the worst said stands.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobOutcomeTests
{
    private static readonly Guid Alice = Guid.Parse("0e4b6c1e-7b52-4f3c-9d2f-1a1a1a1a1a1a");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_warned_job_ends_done_on_its_row_and_warned_with_its_words_for_its_enqueuer()
    {
        await using var h = new JobsHarness();
        await using var heard = await HeardWork.Own(h, Alice);
        await Enqueue(h, new OutcomeJob("w", "warn"));

        await h.RunUntilAsync(() => heard.Heard.Any(w => w.HasEnded));

        var row = await h.Row(await h.IdOf("w"));
        Assert.NotNull(row!.ProcessedAt);
        Assert.Equal(WorkState.Warned, row.Outcome);
        var ended = heard.Heard.Last();
        Assert.Equal(WorkState.Warned, ended.State);
        Assert.Equal("No new rows in prices.csv", ended.Detail);
    }

    [Fact]
    public async Task A_refused_job_fails_for_good_on_its_first_attempt_and_is_no_dead_letter()
    {
        await using var h = new JobsHarness(o => o.MaxAttempts = 5);
        await Enqueue(h, new OutcomeJob("r", "refuse"));
        var id = await h.IdOf("r");

        await h.RunUntilAsync(() => h.Recorder.Values.Contains("r"));
        var row = await h.Processed(id);

        Assert.Equal(1, row.Attempts);
        Assert.Equal(WorkState.Failed, row.Outcome);
        Assert.Equal("prices.csv has no header row", row.Detail);
        Assert.Equal("Refused: prices.csv has no header row", row.Error);
        Assert.Equal(WorkState.Failed, JobWork.StateOf(row, maxAttempts: 5, DateTime.UtcNow));
    }

    [Fact]
    public async Task A_refused_job_that_then_throws_is_still_refused_and_never_retried_and_its_error_is_not_shown()
    {
        await using var h = new JobsHarness(o => o.MaxAttempts = 5);
        await using var heard = await HeardWork.Own(h, Alice);
        await Enqueue(h, new OutcomeJob("rt", "refuse-throw"));
        var id = await h.IdOf("rt");

        await h.RunUntilAsync(() => heard.Heard.Any(w => w.HasEnded));
        var row = await h.Processed(id);

        Assert.Equal(1, row.Attempts);
        Assert.Equal(WorkState.Failed, row.Outcome);
        Assert.Equal(["rt"], h.Recorder.Values);
        Assert.DoesNotContain(heard.Heard, w => w.Detail?.Contains("secret", StringComparison.Ordinal) == true);
        Assert.Equal("prices.csv has no header row", heard.Heard.Last().Detail);
    }

    [Fact]
    public async Task The_worst_outcome_said_stands_and_a_note_gives_way_to_a_warning()
    {
        await using var h = new JobsHarness();
        await Enqueue(h, new OutcomeJob("rw", "refuse-warn"));
        await Enqueue(h, new OutcomeJob("nw", "note-warn"));
        await Enqueue(h, new OutcomeJob("n", "note"));

        await h.RunUntilAsync(() => h.Recorder.Values.Count == 3);
        var refusedThenWarned = await h.Processed(await h.IdOf("rw"));
        var notedThenWarned = await h.Processed(await h.IdOf("nw"));
        var noted = await h.Processed(await h.IdOf("n"));

        Assert.Equal((WorkState.Failed, "prices.csv has no header row"), (refusedThenWarned.Outcome!.Value, refusedThenWarned.Detail));
        Assert.Equal((WorkState.Warned, "Some rows skipped"), (notedThenWarned.Outcome!.Value, notedThenWarned.Detail));
        Assert.Null(noted.Outcome);
        Assert.Equal("1,200 rows kept", noted.Detail);
        Assert.Equal(WorkState.Done, JobWork.StateOf(noted, maxAttempts: 10, DateTime.UtcNow));
    }

    [Fact]
    public async Task An_awaited_job_its_handler_refuses_throws_its_words_to_the_caller()
    {
        await using var h = new JobsHarness();

        await h.Processor.StartAsync(CancellationToken.None);
        JobFailedException failed;
        try
        {
            failed = await Assert.ThrowsAsync<JobFailedException>(async () => await h.Queue.Run(new OutcomeJob("ar", "refuse"), Ct));
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        Assert.Equal("prices.csv has no header row", failed.Message);
    }

    [Fact]
    public void Outside_a_job_the_verbs_do_nothing_and_a_detail_of_no_words_is_refused()
    {
        Jobs.Warn("nothing to warn");
        Jobs.Refuse("nothing to refuse");
        Jobs.Note("nothing to note");

        var empty = Assert.Throws<ArgumentException>(() => Jobs.Note("  "));

        Assert.Equal("detail", empty.ParamName);
    }

    [Fact]
    public void A_detail_longer_than_a_row_keeps_is_cut_there()
    {
        var detail = new string('x', 2000);

        var kept = JobNotice.CutDetail(detail);

        Assert.Equal(JobNotice.MaxDetail, kept.Length);
    }

    [Fact]
    public async Task The_fake_ends_a_refused_job_failed_without_a_throw_and_an_awaited_one_throws_its_words()
    {
        await using var services = new ServiceCollection().AddRaskCqrs()
            .AddSingleton(new Recorder()).BuildServiceProvider();
        using var ambient = Ambient.Enter(services);
        using var jobs = Jobs.Fake();
        using (Current.UseUser(Alice))
        {
            await Jobs.Enqueue(new OutcomeJob("f", "refuse-throw"), Ct).Noticed("Import");
        }

        await jobs.Run(Ct);
        var failed = await Assert.ThrowsAsync<JobFailedException>(async () => await Jobs.Run(new OutcomeJob("g", "refuse"), Ct));

        Assert.Equal("prices.csv has no header row", failed.Message);
    }

    [Theory]
    [InlineData("Shared")]
    [InlineData("Batch")]
    [InlineData("Outcome")]
    [InlineData("Detail")]
    public async Task A_database_without_the_outcome_columns_says_which_commands_to_run(string column)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rask-nooutcome-{Guid.NewGuid():N}.db");
        await using var h = new JobsHarness(dbPath: path);
        await h.Queue.Enqueue(new RecordJob("waiting"), Ct);
        await using (var db = h.NewContext())
        {
#pragma warning disable EF1002 // The column is one of four constants above, not input.
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE Job DROP COLUMN \"{column}\";", cancellationToken: Ct);
#pragma warning restore EF1002
        }

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Logged("rask db add AddJobOutcomes && rask db update");
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
            File.Delete(path);
        }

        Assert.DoesNotContain(h.Logs, l => l.Contains("Job processing cycle failed", StringComparison.Ordinal));
    }

    private static async Task Enqueue(JobsHarness h, IJob job)
    {
        using var who = Current.UseUser(Alice);
        await h.Queue.Enqueue(job, Ct).Noticed("Import of prices.csv");
    }
}
