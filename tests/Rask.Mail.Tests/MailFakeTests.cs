using Rask.Batteries;

namespace Rask.Mail.Tests;

/// <summary>
/// The fake stands in for the battery without a database, so these are pure unit tests — see the exempt
/// list in <see cref="MailDbCollectionGuardTests"/>.
/// </summary>
public sealed class MailFakeTests
{
    private static Email Welcome(string to) =>
        Email.To(to).Subject("Welcome").Html("<p>hello, and <a href=\"#\">unsubscribe</a></p>");

    [Fact]
    public async Task A_fake_takes_every_send_instead_of_the_real_battery()
    {
        using var mail = Mail.Fake();

        await Mail.Send(Welcome("ann@x.io"));

        mail.Sent().To("ann@x.io").Once();
    }

    [Fact]
    public async Task A_send_is_recorded_with_the_delay_it_asked_for()
    {
        using var mail = Mail.Fake();

        await Mail.Send(Welcome("ann@x.io")).In(24.Hours);

        mail.Sent().In(24.Hours).Once();
        mail.Sent().In(1.Hour).None();
    }

    [Fact]
    public async Task A_send_is_recorded_with_the_moment_it_asked_for()
    {
        var midnight = new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
        using var mail = Mail.Fake();

        await Mail.Send(Welcome("ann@x.io")).At(midnight);

        mail.Sent().At(midnight).Once();
    }

    [Fact]
    public async Task The_steps_narrow_together()
    {
        using var mail = Mail.Fake();

        await Mail.Send(Welcome("ann@x.io"));
        await Mail.Send(Email.To("bo@x.io").Subject("Receipt").Html("<p>thanks</p>"));

        mail.Sent().Twice();
        mail.Sent().To("ann@x.io").WithSubject("Welcome").Once();
        mail.Sent().To("bo@x.io").WithSubject("Welcome").None();
    }

    [Fact]
    public async Task The_body_is_reachable_for_what_the_steps_do_not_cover()
    {
        using var mail = Mail.Fake();

        await Mail.Send(Welcome("ann@x.io"));

        var sent = mail.Sent().To("ann@x.io").Single();
        Assert.Contains("unsubscribe", sent.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failure_names_what_was_sent_instead()
    {
        using var mail = Mail.Fake();

        await Mail.Send(Welcome("bo@x.io"));
        await Mail.Send(Welcome("cy@x.io"));

        var error = Assert.Throws<CountingException>(() => mail.Sent().To("ann@x.io").Once());
        Assert.Contains("Expected one email to \"ann@x.io\"", error.Message, StringComparison.Ordinal);
        Assert.Contains("bo@x.io", error.Message, StringComparison.Ordinal);
        Assert.Contains("cy@x.io", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_with_nothing_sent_says_so_rather_than_listing_nothing()
    {
        using var mail = Mail.Fake();

        var error = Assert.Throws<CountingException>(() => mail.Sent().Once());

        Assert.Contains("Nothing was sent at all.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disposing_the_fake_puts_the_real_battery_back()
    {
        using (var mail = Mail.Fake())
        {
            await Mail.Send(Welcome("ann@x.io"));
            mail.Sent().Once();
        }

        // Nothing stands in the way now, and no app is in progress, so the facade says which line compiles.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await Mail.Send(Welcome("ann@x.io")));
        Assert.Contains("Inject IMail", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_injected_IMail_can_be_the_fake_too()
    {
        using var mail = Mail.Fake();
        IMail injected = mail;

        await injected.Send(Welcome("ann@x.io")).In(2.Hours);

        mail.Sent().To("ann@x.io").In(2.Hours).Once();
    }
}
