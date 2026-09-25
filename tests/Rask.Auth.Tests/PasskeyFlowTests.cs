using System.Buffers.Text;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wire;

namespace Rask.Auth.Tests;

/// <summary>
/// Adding a passkey and signing in with one, against a real database and the real challenge state.
/// </summary>
/// <remarks>
/// <see cref="PasskeyVerifierTests" /> covers the cryptography on its own. What is here is everything around it: the
/// sealed challenge, whose account a ceremony belongs to, what a removed passkey can still do, and the throttle.
/// </remarks>
[Collection(AuthDbCollection.Name)]
public sealed class PasskeyFlowTests
{
    private const string Password = "Password1";
    private const string Owner = "owner@example.com";
    private const string RelyingPartyId = "rask.test";

    [Fact]
    public async Task A_passkey_is_added_and_then_signs_its_owner_in()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;
        using var authenticator = new TestAuthenticator();

        var added = await AddAsync(harness, owner.Id, authenticator, "MacBook");
        Assert.True(added.Succeeded, $"adding failed: {added.Error} {added.Message}");

        await using (var db = harness.NewContext())
        {
            var stored = await db.Set<Passkey>().SingleAsync(p => p.UserId == owner.Id);
            Assert.Equal("MacBook", stored.Name);
            Assert.Equal(authenticator.CredentialId, stored.CredentialId);
            Assert.Equal("internal", stored.Transports);
        }

        authenticator.SignCount = 1;
        var outcome = await SignInAsync(harness, authenticator, owner.Id);

        Assert.True(outcome.Result.Succeeded, $"sign-in failed: {outcome.Result.Error}");
        Assert.NotNull(outcome.Principal);
        Assert.Equal(owner.Id.ToString(), outcome.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(Owner, outcome.Principal.Identity!.Name);
    }

    /// <summary>A signature counter that moved is written back, so the next clone check has the real number.</summary>
    [Fact]
    public async Task Signing_in_records_the_counter_and_the_time()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;
        using var authenticator = new TestAuthenticator();

        await AddAsync(harness, owner.Id, authenticator);
        authenticator.SignCount = 42;
        Assert.True((await SignInAsync(harness, authenticator, owner.Id)).Result.Succeeded);

        await using var db = harness.NewContext();
        var stored = await db.Set<Passkey>().SingleAsync(p => p.UserId == owner.Id);
        Assert.Equal(42u, stored.SignCount);
        Assert.NotNull(stored.LastUsedAt);
    }

    [Fact]
    public async Task The_same_credential_cannot_be_registered_twice()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;
        using var authenticator = new TestAuthenticator();

        Assert.True((await AddAsync(harness, owner.Id, authenticator)).Succeeded);

        var again = await AddAsync(harness, owner.Id, authenticator);

        Assert.False(again.Succeeded);
        Assert.Equal(AuthError.PasskeyRejected, again.Error);
    }

    [Fact]
    public async Task A_removed_passkey_no_longer_signs_anybody_in()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;
        using var authenticator = new TestAuthenticator();

        await AddAsync(harness, owner.Id, authenticator);

        Guid passkeyId;
        await using (var db = harness.NewContext())
        {
            passkeyId = (await db.Set<Passkey>().SingleAsync(p => p.UserId == owner.Id)).Id;
        }

        using (var scope = harness.NewScope())
        {
            var removed = await scope.ServiceProvider
                .GetRequiredService<AccountService<TestUser>>()
                .RemovePasskeyAsync(owner.Id, passkeyId);
            Assert.True(removed.Succeeded);
        }

        authenticator.SignCount = 1;
        Assert.False((await SignInAsync(harness, authenticator, owner.Id)).Result.Succeeded);
    }

    /// <summary>
    /// A removed passkey keeps its row, and a credential id is unique, so this is the case that would otherwise make
    /// re-adding a device impossible forever.
    /// </summary>
    [Fact]
    public async Task The_same_device_can_be_added_again_after_being_removed()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;
        using var authenticator = new TestAuthenticator();

        await AddAsync(harness, owner.Id, authenticator);

        Guid passkeyId;
        await using (var db = harness.NewContext())
        {
            passkeyId = (await db.Set<Passkey>().SingleAsync(p => p.UserId == owner.Id)).Id;
        }

        using (var scope = harness.NewScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<AccountService<TestUser>>()
                .RemovePasskeyAsync(owner.Id, passkeyId);
        }

        var again = await AddAsync(harness, owner.Id, authenticator, "MacBook again");

        Assert.True(again.Succeeded, $"re-adding failed: {again.Error} {again.Message}");

        authenticator.SignCount = 1;
        Assert.True((await SignInAsync(harness, authenticator, owner.Id)).Result.Succeeded);
    }

    [Fact]
    public async Task Somebody_elses_passkey_cannot_be_removed()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;
        using var authenticator = new TestAuthenticator();

        await AddAsync(harness, owner.Id, authenticator);
        await RegisterAsync(harness, "other@example.com");
        var other = (await harness.UserAsync("other@example.com"))!;

        Guid passkeyId;
        await using (var db = harness.NewContext())
        {
            passkeyId = (await db.Set<Passkey>().SingleAsync(p => p.UserId == owner.Id)).Id;
        }

        using var scope = harness.NewScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<AccountService<TestUser>>()
            .RemovePasskeyAsync(other.Id, passkeyId);

        Assert.False(result.Succeeded);

        await using var check = harness.NewContext();
        Assert.True(await check.Set<Passkey>().AnyAsync(p => p.UserId == owner.Id));
    }

    /// <summary>The state says whose ceremony it is, so it cannot be carried to another account.</summary>
    [Fact]
    public async Task A_challenge_issued_to_one_account_cannot_add_a_passkey_to_another()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;
        await RegisterAsync(harness, "other@example.com");
        var other = (await harness.UserAsync("other@example.com"))!;

        using var authenticator = new TestAuthenticator();
        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<TestUser>>();

        var challenge = await accounts.BeginAddPasskeyAsync(owner.Id, AuthHarness.Origin);
        Assert.NotNull(challenge);

        var result = await accounts.CompleteAddPasskeyAsync(
            other.Id,
            authenticator.Register(
                RelyingPartyId,
                AuthHarness.Origin,
                Base64Url.DecodeFromChars(challenge.Challenge),
                challenge.State),
            AuthHarness.Origin);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.InvalidToken, result.Error);
    }

    /// <summary>Sealed state and nothing else: a challenge this server did not issue opens nothing.</summary>
    [Fact]
    public async Task A_forged_state_is_refused()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;

        using var authenticator = new TestAuthenticator();
        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<TestUser>>();

        var result = await accounts.CompleteAddPasskeyAsync(
            owner.Id,
            authenticator.Register(RelyingPartyId, AuthHarness.Origin, new byte[32], "not-a-real-state"),
            AuthHarness.Origin);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.InvalidToken, result.Error);
    }

    /// <summary>A ceremony is good once. Replaying a captured one inside its lifetime must not sign anybody in.</summary>
    [Fact]
    public async Task A_replayed_sign_in_is_refused()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;
        using var authenticator = new TestAuthenticator();

        await AddAsync(harness, owner.Id, authenticator);
        authenticator.SignCount = 1;

        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<TestUser>>();

        var challenge = accounts.BeginPasskeySignIn(AuthHarness.Origin);
        Assert.NotNull(challenge);

        var assertion = authenticator.SignIn(
            RelyingPartyId,
            AuthHarness.Origin,
            Base64Url.DecodeFromChars(challenge.Challenge),
            owner.Id,
            challenge.State);

        Assert.True((await accounts.CompletePasskeySignInAsync(assertion, AuthHarness.Origin, null)).Result.Succeeded);

        var replayed = await accounts.CompletePasskeySignInAsync(assertion, AuthHarness.Origin, null);

        Assert.False(replayed.Result.Succeeded);
        Assert.Equal(AuthError.InvalidCredentials, replayed.Result.Error);
    }

    /// <summary>A passkey that was never registered is refused exactly like a wrong password.</summary>
    [Fact]
    public async Task An_unknown_credential_is_refused_as_invalid_credentials()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;
        using var stranger = new TestAuthenticator();

        var outcome = await SignInAsync(harness, stranger, owner.Id);

        Assert.False(outcome.Result.Succeeded);
        Assert.Equal(AuthError.InvalidCredentials, outcome.Result.Error);
    }

    /// <summary>Guessing is throttled per client, the way password guessing is.</summary>
    [Fact]
    public async Task Repeated_failures_from_one_client_are_throttled()
    {
        await using var harness = await ClaimedAsync(o => o.SignInAttemptsPerMinute = 3);
        var owner = (await harness.UserAsync(Owner))!;
        using var stranger = new TestAuthenticator();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(
                AuthError.InvalidCredentials,
                (await SignInAsync(harness, stranger, owner.Id, "10.0.0.9")).Result.Error);
        }

        var throttled = await SignInAsync(harness, stranger, owner.Id, "10.0.0.9");

        Assert.Equal(AuthError.TooManyAttempts, throttled.Result.Error);
    }

    [Fact]
    public async Task Turning_passkeys_off_refuses_both_halves()
    {
        await using var harness = await ClaimedAsync(o => o.Passkeys = false);
        var owner = (await harness.UserAsync(Owner))!;

        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<TestUser>>();

        Assert.Null(await accounts.BeginAddPasskeyAsync(owner.Id, AuthHarness.Origin));
        Assert.Null(accounts.BeginPasskeySignIn(AuthHarness.Origin));
    }

    [Fact]
    public async Task A_password_reset_takes_back_every_passkey_on_the_account()
    {
        // Whoever registered the address first, or knew the old password, may have added a passkey of their own.
        // The real owner resetting is taking the account back, so none of them may still sign in afterwards.
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!;
        using var authenticator = new TestAuthenticator();
        Assert.True((await AddAsync(harness, owner.Id, authenticator)).Succeeded);

        using (var scope = harness.NewScope())
        {
            var token = scope.ServiceProvider.GetRequiredService<AuthTokens>().ForReset(owner, TimeSpan.FromHours(1));
            var reset = await scope.ServiceProvider.GetRequiredService<AccountService<TestUser>>()
                .ResetPasswordAsync(owner.Id.ToString(), token, "NewPassword1");
            Assert.True(reset.Succeeded, $"reset failed: {reset.Error}");
        }

        authenticator.SignCount = 1;
        var outcome = await SignInAsync(harness, authenticator, owner.Id);

        Assert.False(outcome.Result.Succeeded);
        await using var db = harness.NewContext();
        Assert.False(await db.Set<Passkey>().AnyAsync(p => p.UserId == owner.Id));
    }

    [Fact]
    public async Task An_account_whose_address_is_unconfirmed_cannot_add_a_passkey()
    {
        await using var harness = await ClaimedAsync();
        await RegisterAsync(harness, "squatter@example.com");
        var squatter = (await harness.UserAsync("squatter@example.com"))!;

        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<TestUser>>();

        var challenge = await accounts.BeginAddPasskeyAsync(squatter.Id, AuthHarness.Origin);

        Assert.Null(challenge);
        Assert.Equal(AuthError.EmailNotConfirmed, await accounts.PasskeyRefusalAsync(squatter.Id));
    }

    private static async Task<AuthResult> AddAsync(
        AuthHarness harness, Guid userId, TestAuthenticator authenticator, string? name = null)
    {
        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<TestUser>>();

        var challenge = await accounts.BeginAddPasskeyAsync(userId, AuthHarness.Origin);
        Assert.NotNull(challenge);
        Assert.Equal(RelyingPartyId, challenge.RelyingPartyId);

        // The user handle is the account id, never the address.
        Assert.Equal(userId, new Guid(Base64Url.DecodeFromChars(challenge.UserId)));

        return await accounts.CompleteAddPasskeyAsync(
            userId,
            authenticator.Register(
                RelyingPartyId,
                AuthHarness.Origin,
                Base64Url.DecodeFromChars(challenge.Challenge),
                challenge.State,
                name),
            AuthHarness.Origin);
    }

    private static async Task<AccountOutcome> SignInAsync(
        AuthHarness harness, TestAuthenticator authenticator, Guid userId, string? client = null)
    {
        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<TestUser>>();

        var challenge = accounts.BeginPasskeySignIn(AuthHarness.Origin);
        Assert.NotNull(challenge);

        return await accounts.CompletePasskeySignInAsync(
            authenticator.SignIn(
                RelyingPartyId,
                AuthHarness.Origin,
                Base64Url.DecodeFromChars(challenge.Challenge),
                userId,
                challenge.State),
            AuthHarness.Origin,
            client);
    }

    private static async Task<AuthHarness> ClaimedAsync(Action<AuthOptions>? configure = null)
    {
        var harness = new AuthHarness(configure);
        await harness.StartAsync();

        var owner = await RegisterAsync(harness, Owner, AuthHarness.FirstRunTokenValue);
        Assert.True(owner.Succeeded, $"harness setup failed: {owner.Error} {owner.Message}");

        // Only a confirmed address may add a passkey, so the owner proves theirs first.
        await using var db = harness.NewContext();
        var user = await db.Set<TestUser>().SingleAsync(u => u.Email == Owner);
        user.ConfirmEmail(DateTime.UtcNow);
        await db.SaveChangesAsync();

        return harness;
    }

    private static async Task<AuthResult> RegisterAsync(
        AuthHarness harness, string email, string? firstRunToken = null)
    {
        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<TestUser>>();
        return (await accounts.RegisterAsync(email, Password, firstRunToken, client: null)).Result;
    }
}
