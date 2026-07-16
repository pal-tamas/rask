using Microsoft.EntityFrameworkCore;

namespace Rask.Mail.Tests;

public sealed class MailProcessorTests
{
    private static Email SampleEmail() =>
        Email.To("ada@example.com", "Ada").Subject("Welcome").Body("<p>hi</p>");

    [Fact]
    public async Task Send_delivers_the_email_and_marks_it_processed()
    {
        await using var harness = new MailHarness();
        await harness.Processor.StartAsync(CancellationToken.None);
        try
        {
            await harness.Queue.SendAsync(SampleEmail());
            await harness.WaitUntilAsync(async () => harness.Sender.Sent.Count == 1);

            var sent = Assert.Single(harness.Sender.Sent);
            Assert.Equal("Welcome", sent.Subject);
            Assert.Equal("ada@example.com", Assert.Single(sent.To).Address);

            var row = await harness.SingleMailAsync();
            Assert.NotNull(row.ProcessedAt);
            Assert.Null(row.Error);
            Assert.Equal(0, row.Attempts);
        }
        finally
        {
            await harness.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Send_defaults_the_sender_from_options_and_renders_a_component_body()
    {
        await using var harness = new MailHarness();
        await harness.Processor.StartAsync(CancellationToken.None);
        try
        {
            await harness.Queue.SendAsync(Email.To("ada@example.com").Subject("Hi").Body(new GreetingEmail("Ada")));
            await harness.WaitUntilAsync(async () => harness.Sender.Sent.Count == 1);

            var sent = Assert.Single(harness.Sender.Sent);
            Assert.Equal("noreply@example.com", sent.From.Address);
            Assert.Equal("Example", sent.From.Name);
            Assert.Contains("Hello, Ada!", sent.HtmlBody);
        }
        finally
        {
            await harness.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_batch_of_emails_is_each_delivered_and_marked()
    {
        await using var harness = new MailHarness();
        await harness.Processor.StartAsync(CancellationToken.None);
        try
        {
            for (var i = 0; i < 5; i++)
            {
                await harness.Queue.SendAsync(Email.To($"user{i}@example.com").Subject($"#{i}").Body("<p>hi</p>"));
            }

            await harness.WaitUntilAsync(async () => harness.Sender.Sent.Count == 5);

            await using var db = harness.NewContext();
            Assert.Equal(5, await db.Set<QueuedMail>().CountAsync(m => m.ProcessedAt != null));
        }
        finally
        {
            await harness.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Scheduled_email_is_not_delivered_before_its_run_time()
    {
        await using var harness = new MailHarness();
        await harness.Processor.StartAsync(CancellationToken.None);
        try
        {
            await harness.Queue.ScheduleAsync(SampleEmail(), delay: TimeSpan.FromHours(1));

            // The clock is not advanced, so the message stays in the future — give the poll loop time to prove it.
            await Task.Delay(200);
            Assert.Empty(harness.Sender.Sent);

            harness.Clock.Advance(TimeSpan.FromHours(1));
            await harness.WaitUntilAsync(async () => harness.Sender.Sent.Count == 1);
        }
        finally
        {
            await harness.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Failing_send_is_retried_with_backoff_then_delivered()
    {
        var sender = new RecordingMailSender { FailFirst = 2 };
        await using var harness = new MailHarness(o => o.BaseRetryDelay = TimeSpan.FromSeconds(1), sender);
        await harness.Processor.StartAsync(CancellationToken.None);
        try
        {
            await harness.Queue.SendAsync(SampleEmail());
            await harness.WaitUntilAsync(async () => sender.Sent.Count == 1, advanceClock: true);

            Assert.Equal(3, sender.Attempts); // two failures then success

            var row = await harness.SingleMailAsync();
            Assert.NotNull(row.ProcessedAt);
            Assert.Equal(2, row.Attempts);
        }
        finally
        {
            await harness.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Permanently_failing_email_dead_letters_after_max_attempts()
    {
        var sender = new RecordingMailSender { AlwaysFail = true };
        await using var harness = new MailHarness(o =>
        {
            o.MaxAttempts = 3;
            o.BaseRetryDelay = TimeSpan.FromSeconds(1);
        }, sender);
        await harness.Processor.StartAsync(CancellationToken.None);
        try
        {
            await harness.Queue.SendAsync(SampleEmail());
            await harness.WaitUntilAsync(async () => (await harness.SingleMailAsync()).Attempts == 3, advanceClock: true);

            // Once at MaxAttempts the message is a dead letter — no longer claimed, never delivered.
            var attemptsAtDeadLetter = sender.Attempts;
            harness.Clock.Advance(TimeSpan.FromHours(2));
            await Task.Delay(200);

            Assert.Equal(attemptsAtDeadLetter, sender.Attempts);
            Assert.Empty(sender.Sent);
            var row = await harness.SingleMailAsync();
            Assert.Null(row.ProcessedAt);
            Assert.Equal(3, row.Attempts);
            Assert.Equal("smtp boom", row.Error);
        }
        finally
        {
            await harness.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Sent_email_is_purged_after_the_retention_period()
    {
        await using var harness = new MailHarness(o => o.RetentionPeriod = TimeSpan.FromMinutes(1));
        await harness.Processor.StartAsync(CancellationToken.None);
        try
        {
            await harness.Queue.SendAsync(SampleEmail());
            await harness.WaitUntilAsync(async () => harness.Sender.Sent.Count == 1);

            // Advance past retention + the 1h purge throttle so the next purge tick removes the sent row.
            harness.Clock.Advance(TimeSpan.FromHours(2));
            await harness.WaitUntilAsync(async () => await harness.CountMailAsync() == 0);
        }
        finally
        {
            await harness.Processor.StopAsync(CancellationToken.None);
        }
    }
}
