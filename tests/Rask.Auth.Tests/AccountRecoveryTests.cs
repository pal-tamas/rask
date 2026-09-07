using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Rask.Auth.Pages;
using Rask.Core.Authentication;
using Rask.Testing;

namespace Rask.Auth.Tests;

/// <summary>Confirming an address, and resetting a password, end to end over a real database.</summary>
/// <remarks>
///     These drive <see cref="AccountService{TUser}" /> rather than the endpoints, because the properties
///     worth pinning are about the account store: what a token unlocks, what it does not, and what the
///     app is willing to say about an address it has never seen.
/// </remarks>
[Collection(AuthDbCollection.Name)]
public sealed class AccountRecoveryTests
{
    private const string Password = "Password1";
    private const string NewPassword = "Password2longer";
    private const string Owner = "owner@example.com";

    [Fact]
    public async Task Registering_emails_a_confirmation_link()
    {
        await using var harness = await ClaimedAsync();

        var sent = harness.Mail!.LastTo(Owner);

        Assert.NotNull(sent);
        Assert.Contains("/confirm-email", sent.Link ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_confirmation_link_confirms_the_address()
    {
        await using var harness = await ClaimedAsync();
        var (userId, token) = Parse(harness.Mail!.LastTo(Owner)!.Link!);

        var result = await ConfirmAsync(harness, userId, token);

        Assert.True(result.Succeeded, $"confirm failed: {result.Error}");
        Assert.True(await IsConfirmedAsync(harness, Owner));
    }

    [Fact]
    public async Task A_tampered_confirmation_token_is_refused()
    {
        await using var harness = await ClaimedAsync();
        var (userId, token) = Parse(harness.Mail!.LastTo(Owner)!.Link!);

        var result = await ConfirmAsync(harness, userId, token + "x");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.InvalidToken, result.Error);
        Assert.False(await IsConfirmedAsync(harness, Owner));
    }

    [Fact]
    public async Task A_confirmation_token_does_not_work_on_another_account()
    {
        await using var harness = await ClaimedAsync();
        await RegisterAsync(harness, "second@example.com");

        var (_, ownersToken) = Parse(harness.Mail!.LastTo(Owner)!.Link!);
        var (secondsId, _) = Parse(harness.Mail!.LastTo("second@example.com")!.Link!);

        // Identity binds a token to the user it was minted for. Worth pinning rather than assuming:
        // a token that travelled between accounts would let one registration confirm any address.
        var result = await ConfirmAsync(harness, secondsId, ownersToken);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.InvalidToken, result.Error);
    }

    [Fact]
    public async Task A_reset_link_lets_the_new_password_in_and_keeps_the_old_one_out()
    {
        await using var harness = await ClaimedAsync();

        var sent = await RequestResetAsync(harness, Owner);
        var (userId, token) = Parse(sent!.Link!);

        var reset = await ResetAsync(harness, userId, token, NewPassword);
        Assert.True(reset.Succeeded, $"reset failed: {reset.Error} {reset.Message}");

        Assert.True((await SignInAsync(harness, Owner, NewPassword)).Succeeded);
        Assert.False((await SignInAsync(harness, Owner, Password)).Succeeded);
    }

    [Fact]
    public async Task A_reset_token_cannot_be_used_twice()
    {
        await using var harness = await ClaimedAsync();
        var (userId, token) = Parse((await RequestResetAsync(harness, Owner))!.Link!);

        await ResetAsync(harness, userId, token, NewPassword);
        var replay = await ResetAsync(harness, userId, token, "Password3longer");

        // The security stamp moved with the first reset, which is what invalidates the token. If this
        // ever passes, a leaked link stays live for as long as it has not expired.
        Assert.False(replay.Succeeded);
        Assert.Equal(AuthError.InvalidToken, replay.Error);
    }

    [Fact]
    public async Task A_reset_below_the_password_policy_says_so_rather_than_blaming_the_link()
    {
        await using var harness = await ClaimedAsync();
        var (userId, token) = Parse((await RequestResetAsync(harness, Owner))!.Link!);

        var result = await ResetAsync(harness, userId, token, "Ab1");

        // A weak password and a dead link are the same failed IdentityResult, and they need different
        // words: one means "pick a longer password", the other "ask for a new link".
        Assert.Equal(AuthError.WeakPassword, result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task A_completed_reset_also_confirms_the_address()
    {
        await using var harness = await ClaimedAsync();
        Assert.False(await IsConfirmedAsync(harness, Owner));

        var (userId, token) = Parse((await RequestResetAsync(harness, Owner))!.Link!);
        await ResetAsync(harness, userId, token, NewPassword);

        // Otherwise an account made before RequireConfirmedEmail was turned on can reset its password
        // and still not get in, with nothing in the UI to fix it.
        Assert.True(await IsConfirmedAsync(harness, Owner));
    }

    [Fact]
    public async Task An_unknown_address_is_answered_the_same_way_as_a_known_one()
    {
        await using var harness = await ClaimedAsync();

        var known = await SendResetAsync(harness, Owner);
        var unknown = await SendResetAsync(harness, "nobody@example.com");

        // The whole point of the endpoint's answer. A difference here is a membership oracle anybody
        // can walk a list of addresses through.
        Assert.Equal(known.Succeeded, unknown.Succeeded);
        Assert.Equal(known.Error, unknown.Error);
        Assert.True(known.Succeeded);

        // …and nothing was sent to the address that has no account.
        Assert.Null(harness.Mail!.LastTo("nobody@example.com"));
    }

    [Fact]
    public async Task Without_a_mail_battery_a_reset_reports_it_rather_than_pretending()
    {
        await using var harness = await ClaimedAsync(mail: false);

        var result = await SendResetAsync(harness, Owner);

        // A reset that queues nothing looks exactly like one that worked, and the person waiting for
        // the email cannot tell. This is the one case where saying nothing would be worse than the
        // (very small) fact that this app cannot send mail.
        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.MailNotConfigured, result.Error);
    }

    [Fact]
    public async Task RequireConfirmedEmail_keeps_an_unconfirmed_account_out_until_it_confirms()
    {
        await using var harness = await ClaimedAsync(o => o.RequireConfirmedEmail = true);

        var before = await SignInAsync(harness, Owner, Password);
        Assert.False(before.Succeeded);
        Assert.Equal(AuthError.EmailNotConfirmed, before.Error);

        var (userId, token) = Parse(harness.Mail!.LastTo(Owner)!.Link!);
        await ConfirmAsync(harness, userId, token);

        Assert.True((await SignInAsync(harness, Owner, Password)).Succeeded);
    }

    [Fact]
    public async Task A_wrong_password_on_an_unconfirmed_account_still_reads_as_a_wrong_password()
    {
        await using var harness = await ClaimedAsync(o => o.RequireConfirmedEmail = true);

        var result = await SignInAsync(harness, Owner, "WrongPassword1");

        // Answering "confirm your email" here would tell anybody who asked that this address has an
        // account. The confirmation gate is checked after the password for exactly this reason.
        Assert.Equal(AuthError.InvalidCredentials, result.Error);
    }

    [Fact]
    public async Task The_emailed_link_points_at_the_configured_public_origin()
    {
        await using var harness = await ClaimedAsync(o => o.PublicOrigin = "https://app.example.com");

        var link = harness.Mail!.LastTo(Owner)!.Link;

        // Absolute, and on the origin the operator named — never a forwarded host header, which is
        // attacker-controlled and would send a working token to a domain of their choosing.
        Assert.StartsWith("https://app.example.com/confirm-email?", link, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_token_survives_the_round_trip_through_the_query_string()
    {
        await using var harness = await ClaimedAsync();
        var link = harness.Mail!.LastTo(Owner)!.Link!;

        var (userId, token) = Parse(link);

        // Identity's tokens are base64 and routinely carry '+' and '/'. A '+' that reaches the query
        // unencoded arrives as a space and the token silently stops matching — which reads as "the link
        // expired" rather than as an encoding bug, so it is worth pinning that the link round-trips.
        Assert.True((await ConfirmAsync(harness, userId, token)).Succeeded, $"link did not round-trip: {link}");
    }

    /// <summary>A claimed instance with one account, registered and therefore emailed a confirmation.</summary>
    private static async Task<AuthHarness> ClaimedAsync(
        Action<AuthOptions>? configure = null, bool mail = true)
    {
        var harness = new AuthHarness(configure, mail: mail);
        await harness.StartAsync();

        var owner = await RegisterAsync(harness, Owner, AuthHarness.FirstRunTokenValue);
        Assert.True(owner.Succeeded, $"harness setup failed: {owner.Error} {owner.Message}");

        return harness;
    }

    [Fact]
    public async Task A_failed_send_answers_exactly_as_an_unknown_address_does()
    {
        // #1011. The failure branch was only reachable for an address that EXISTS, so answering
        // differently there made the page a membership oracle: send two resets, and the one that errors
        // is the registered account. Needing a misconfigured app to reach it is not a defence —
        // misconfigured is a state an attacker can wait for, and enumeration is permanent once done.
        await using var harness = await ClaimedAsync();
        harness.Mail!.Throws = true;

        var known = await SendResetAsync(harness, Owner);
        var unknown = await SendResetAsync(harness, "nobody@example.com");

        Assert.Equal(known.Succeeded, unknown.Succeeded);
        Assert.Equal(known.Error, unknown.Error);
        Assert.True(known.Succeeded);
    }

    [Fact]
    public async Task An_app_with_no_mail_battery_is_still_told_plainly()
    {
        // The other side of that trade, kept. The probe fires BEFORE any address is looked up, so it is
        // uniform across addresses and still tells an app that cannot send mail at all — which is a fact
        // about the app, not about whether an account exists.
        await using var harness = await ClaimedAsync(mail: false);

        var known = await SendResetAsync(harness, Owner);
        var unknown = await SendResetAsync(harness, "nobody@example.com");

        Assert.Equal(AuthError.MailNotConfigured, known.Error);
        Assert.Equal(known.Error, unknown.Error);
    }

    [Fact]
    public void Rendering_the_confirm_page_does_not_spend_the_token()
    {
        // The actual security property of #1013, and the one a service-level test cannot state: a GET is
        // not a deliberate act by the person the link was sent to. Mail scanners, link previewers,
        // corporate URL-rewriting gateways and prefetchers all fetch it first, and whichever arrived
        // first spent the single-use token — after which the human clicks their own link and is told it
        // did not work, indistinguishably from a real expiry.
        //
        // Asserted against a spy rather than a database, because the claim is precisely "the page does
        // not CALL this on render", and a spy says that without anything else being able to explain it.
        var auth = new ConfirmSpy();
        var services = new ServiceCollection().BuildServiceProvider();

        // ActivatorUtilities, as the router itself constructs a page: the spy goes in as the ctor
        // dependency it takes.
        var page = ActivatorUtilities.CreateInstance<ConfirmEmailPage>(services, auth);
        page.UserId = "u1";
        page.Token = "t1";

        var html = RaskTest.Render(page, services).Html;

        Assert.Equal(0, auth.Confirms);
        Assert.Contains("confirm-submit", html, StringComparison.Ordinal);
    }

    private sealed class ConfirmSpy : IAuth
    {
        public int Confirms { get; private set; }

        public Task<AuthResult> ConfirmEmailAsync(string userId, string token)
        {
            Confirms++;
            return Task.FromResult(AuthResult.Success);
        }

        public Task<AuthResult> RegisterAsync(
            string email, string password, string? returnUrl = null, string? firstRunToken = null) =>
            Task.FromResult(AuthResult.Success);

        public Task<AuthResult> SignInAsync(
            string email, string password, bool remember = false, string? returnUrl = null) =>
            Task.FromResult(AuthResult.Success);

        public Task SignOutAsync(string? returnUrl = null) => Task.CompletedTask;

        public Task<AuthResult> SendPasswordResetAsync(string email) =>
            Task.FromResult(AuthResult.Success);

        public Task<AuthResult> ResetPasswordAsync(string userId, string token, string password) =>
            Task.FromResult(AuthResult.Success);
    }

    [Fact]
    public async Task Confirming_twice_says_so_rather_than_blaming_the_link()
    {
        // #1013. A confirmation token is single-use, so the second arrival — a reload, a Back, a link
        // opened twice — reported "that link did not work" about an address that is confirmed, and sent
        // the visitor to request another one, which does the same thing.
        await using var harness = await ClaimedAsync(o => o.RequireConfirmedEmail = true);
        var (userId, token) = Parse(harness.Mail!.LastTo(Owner)!.Link!);

        Assert.True((await ConfirmAsync(harness, userId, token)).Succeeded);

        var again = await ConfirmAsync(harness, userId, token);

        Assert.False(again.Succeeded);
        Assert.Equal(AuthError.EmailAlreadyConfirmed, again.Error);
        Assert.True(await IsConfirmedAsync(harness, Owner));
    }

    private static async Task<AuthResult> RegisterAsync(
        AuthHarness harness, string email, string? firstRunToken = null)
    {
        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<RaskUser>>();
        return (await accounts.RegisterAsync(email, Password, firstRunToken)).Result;
    }

    private static async Task<AuthResult> SignInAsync(AuthHarness harness, string email, string password)
    {
        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<RaskUser>>();
        return (await accounts.ValidateAsync(email, password)).Result;
    }

    private static async Task<AuthResult> SendResetAsync(AuthHarness harness, string email)
    {
        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<RaskUser>>();
        return await accounts.SendPasswordResetAsync(email);
    }

    /// <summary>Asks for a reset and returns the message that went out.</summary>
    private static async Task<SentMail?> RequestResetAsync(AuthHarness harness, string email)
    {
        var result = await SendResetAsync(harness, email);
        Assert.True(result.Succeeded, $"reset request failed: {result.Error}");

        var sent = harness.Mail!.LastTo(email);
        Assert.NotNull(sent);
        Assert.Contains("/reset-password", sent.Link ?? "", StringComparison.Ordinal);

        return sent;
    }

    private static async Task<AuthResult> ResetAsync(
        AuthHarness harness, string userId, string token, string password)
    {
        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<RaskUser>>();
        return await accounts.ResetPasswordAsync(userId, token, password);
    }

    private static async Task<AuthResult> ConfirmAsync(AuthHarness harness, string userId, string token)
    {
        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<RaskUser>>();
        return await accounts.ConfirmEmailAsync(userId, token);
    }

    private static async Task<bool> IsConfirmedAsync(AuthHarness harness, string email)
    {
        using var scope = harness.NewScope();
        var users = scope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<RaskUser>>();

        var user = await users.FindByEmailAsync(email);
        return user is not null && await users.IsEmailConfirmedAsync(user);
    }

    /// <summary>Reads the two values back out of a link, the way the landing page's query params do.</summary>
    private static (string UserId, string Token) Parse(string link)
    {
        var query = HttpUtility.ParseQueryString(new Uri(link).Query);
        return (query["userId"] ?? "", query["token"] ?? "");
    }
}
