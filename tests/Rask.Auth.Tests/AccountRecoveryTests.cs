using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;

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
