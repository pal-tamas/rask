namespace Rask.Mailing.Tests;

/// <summary>
///     What happens to a send that is already talking to the SMTP server when the host is asked to stop.
///     This is the battery where the grace period earns the most: delivery and the row update are not one
///     transaction, so a send cancelled mid-conversation may already have been accepted by the server while
///     the row still reads unsent — and the next boot sends it again.
/// </summary>
[Collection(MailDbCollection.Name)]
public sealed class MailShutdownGraceTests
{
    [Fact]
    public async Task An_in_flight_send_finishes_within_the_grace()
    {
        var sender = new RecordingMailSender
        {
            Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var h = new MailHarness(o => o.ShutdownGracePeriod = TimeSpan.FromSeconds(5), sender);
        await h.Queue.Send(Email.To("ada@example.com").Subject("Hi").Html("<p>hi</p>"), TestContext.Current.CancellationToken);

        await h.Processor.StartAsync(CancellationToken.None);
        await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var stop = h.Processor.StopAsync(CancellationToken.None);
        sender.Release!.SetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Single(sender.Sent);
        var mail = await h.SingleMailAsync();
        Assert.NotNull(mail.ProcessedAt);
        // 1, not 0: the claim counts attempts *started*, so mail that sends first time shows one. The
        // roll-back on the shutdown path only applies to work that did NOT finish.
        Assert.Equal(1, mail.Attempts);
    }

    [Fact]
    public async Task A_grace_expiry_leaves_the_row_eligible_without_counting_an_attempt()
    {
        // The send is cancelled and the row stays unsent, so it goes out again on the next boot. That is
        // the deliberate choice: a duplicate email is an annoyance, a lost transactional email is a bug.
        var sender = new RecordingMailSender
        {
            Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var h = new MailHarness(o => o.ShutdownGracePeriod = TimeSpan.FromMilliseconds(50), sender);
        await h.Queue.Send(Email.To("ada@example.com").Subject("Hi").Html("<p>hi</p>"), TestContext.Current.CancellationToken);

        await h.Processor.StartAsync(CancellationToken.None);
        await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await h.Processor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var mail = await h.SingleMailAsync();
        Assert.Null(mail.ProcessedAt);
        // The claim increments Attempts up front, so the shutdown path has to give it back — otherwise
        // MaxAttempts becomes a function of deploy cadence rather than of failure.
        Assert.Equal(0, mail.Attempts);
        Assert.Null(mail.Error);
        // The lease goes back too, so the next boot sees the row at once instead of waiting out a claim
        // held by a process that no longer exists.
        Assert.Null(mail.ClaimToken);
        Assert.Null(mail.ClaimedUntil);
    }

    [Fact]
    public async Task Mail_grace_defaults_to_double_the_other_batteries()
    {
        // 10s rather than 5s, and deliberately: an interrupted send is a possible duplicate, not a clean
        // retry, so halving the interruption rate is worth five extra seconds on an unlucky redeploy.
        Assert.Equal(TimeSpan.FromSeconds(10), new MailOptions().ShutdownGracePeriod);
    }

    [Fact]
    public void A_negative_grace_is_rejected_at_registration()
    {
        var options = new MailOptions { From = "a@example.com", ShutdownGracePeriod = TimeSpan.FromSeconds(-1) };

        var result = new MailOptionsValidator().Validate(null, options);

        Assert.Contains("Rask:Mail:ShutdownGracePeriod", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_grace_CancelAfter_cannot_take_is_rejected_at_registration()
    {
        // CancellationTokenSource.CancelAfter throws above int.MaxValue ms — and it would throw from the
        // shutdown path, the worst place to find out.
        var options = new MailOptions { From = "a@example.com", ShutdownGracePeriod = TimeSpan.FromDays(30) };

        var result = new MailOptionsValidator().Validate(null, options);

        Assert.Contains("Rask:Mail:ShutdownGracePeriod", result.FailureMessage, StringComparison.Ordinal);
    }
}
