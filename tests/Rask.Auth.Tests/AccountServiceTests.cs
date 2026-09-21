using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Wire;

namespace Rask.Auth.Tests;

/// <summary>Registering and signing in, once the instance has been claimed.</summary>
[Collection(AuthDbCollection.Name)]
public sealed class AccountServiceTests
{
    private const string Password = "Password1";

    [Fact]
    public async Task A_registered_account_can_sign_in()
    {
        await using var harness = await ClaimedAsync();

        var result = await SignInAsync(harness, "owner@example.com", Password);

        Assert.True(result.Succeeded, $"sign-in failed: {result.Error} {result.Message}");
    }

    [Fact]
    public async Task A_wrong_password_is_refused()
    {
        await using var harness = await ClaimedAsync();

        var result = await SignInAsync(harness, "owner@example.com", "WrongPassword1");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.InvalidCredentials, result.Error);
    }

    [Fact]
    public async Task An_unknown_address_is_refused_the_same_way_as_a_wrong_password()
    {
        await using var harness = await ClaimedAsync();

        var unknown = await SignInAsync(harness, "nobody@example.com", Password);
        var wrong = await SignInAsync(harness, "owner@example.com", "WrongPassword1");

        // Saying "no such account" would turn sign-in into an account-existence oracle.
        Assert.Equal(wrong.Error, unknown.Error);
        Assert.Equal(AuthError.InvalidCredentials, unknown.Error);
    }

    [Fact]
    public async Task Registering_the_same_address_twice_is_refused()
    {
        await using var harness = await ClaimedAsync();

        var again = await RegisterAsync(harness, "owner@example.com");

        Assert.False(again.Succeeded);
        Assert.Equal(AuthError.DuplicateAccount, again.Error);
    }

    [Fact]
    public async Task A_password_below_the_minimum_is_refused_and_says_why()
    {
        await using var harness = await ClaimedAsync();

        var result = await RegisterAsync(harness, "short@example.com", password: "Ab1");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.WeakPassword, result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Message), "a policy failure must say what was wrong");
    }

    [Fact]
    public async Task Too_many_wrong_passwords_throttle_that_client_not_the_account()
    {
        await using var harness = await ClaimedAsync(o => o.SignInAttemptsPerMinute = 3);

        for (var i = 0; i < 3; i++)
        {
            await SignInAsync(harness, "owner@example.com", "WrongPassword1", client: "10.0.0.1");
        }

        // That client waits out the minute, even with the right password...
        var throttled = await SignInAsync(harness, "owner@example.com", Password, client: "10.0.0.1");
        Assert.False(throttled.Succeeded);
        Assert.Equal(AuthError.TooManyAttempts, throttled.Error);

        // ...and the owner, signing in from anywhere else, is not locked out by somebody guessing.
        var owner = await SignInAsync(harness, "owner@example.com", Password, client: "10.0.0.2");
        Assert.True(owner.Succeeded);
    }

    [Fact]
    public async Task Concurrent_wrong_passwords_cannot_outrun_the_throttle()
    {
        // #1121: the throttle was checked and counted in two steps, so guesses fired together all passed the
        // check before any of them was counted — a guesser who parallelised got as many tries as they liked.
        await using var harness = await ClaimedAsync(o => o.SignInAttemptsPerMinute = 3);

        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ =>
            Task.Run(() => SignInAsync(harness, "owner@example.com", "WrongPassword1", client: "10.0.0.1"))));

        Assert.Equal(3, results.Count(r => r.Error == AuthError.InvalidCredentials));
        Assert.Equal(21, results.Count(r => r.Error == AuthError.TooManyAttempts));
    }

    [Fact]
    public void An_attempt_that_did_not_fail_is_handed_back()
    {
        var throttle = new AuthThrottle(TimeProvider.System) { Limit = 2 };

        for (var i = 0; i < 5; i++)
        {
            using var attempt = throttle.Begin("k");
            Assert.False(attempt.IsThrottled);
        }

        throttle.Begin("k").Fail();
        throttle.Begin("k").Fail();
        Assert.True(throttle.Begin("k").IsThrottled);
    }

    [Fact]
    public void An_open_attempt_counts_against_the_attempts_running_beside_it()
    {
        var throttle = new AuthThrottle(TimeProvider.System) { Limit = 2 };

        using var first = throttle.Begin("k");
        using var second = throttle.Begin("k");

        Assert.True(throttle.Begin("k").IsThrottled);
    }

    [Fact]
    public void A_checked_attempt_counts_only_when_it_fails()
    {
        var throttle = new AuthThrottle(TimeProvider.System) { Limit = 2 };

        using var first = throttle.Check("k");
        using var second = throttle.Check("k");
        using var third = throttle.Check("k");
        Assert.False(third.IsThrottled);

        first.Fail();
        second.Fail();
        Assert.True(throttle.Check("k").IsThrottled);
    }

    [Fact]
    public void A_success_keeps_the_guesses_still_running_beside_it()
    {
        // The owner signs in while a guess from the same client is still being checked. The success forgets
        // the failures BEFORE it; the guess that fails after it must still count.
        var throttle = new AuthThrottle(TimeProvider.System) { Limit = 2 };

        var guess = throttle.Begin("k");
        using (var owner = throttle.Begin("k"))
        {
            owner.Succeed();
        }

        guess.Fail();

        Assert.False(throttle.Begin("k").IsThrottled);
        Assert.True(throttle.Begin("k").IsThrottled);
    }

    [Fact]
    public void A_success_clears_every_failure_and_disposing_it_takes_nothing_else()
    {
        var throttle = new AuthThrottle(TimeProvider.System) { Limit = 2 };
        throttle.Begin("k").Fail();

        using (var winner = throttle.Begin("k"))
        {
            winner.Succeed();
        }

        throttle.Begin("k").Fail();
        throttle.Begin("k").Fail();
        Assert.True(throttle.Begin("k").IsThrottled);
    }

    [Fact]
    public async Task A_successful_sign_in_clears_the_failures_before_it()
    {
        await using var harness = await ClaimedAsync(o => o.SignInAttemptsPerMinute = 3);

        await SignInAsync(harness, "owner@example.com", "WrongPassword1", client: "10.0.0.1");
        await SignInAsync(harness, "owner@example.com", "WrongPassword1", client: "10.0.0.1");
        Assert.True((await SignInAsync(harness, "owner@example.com", Password, client: "10.0.0.1")).Succeeded);

        await SignInAsync(harness, "owner@example.com", "WrongPassword1", client: "10.0.0.1");
        await SignInAsync(harness, "owner@example.com", "WrongPassword1", client: "10.0.0.1");
        Assert.True((await SignInAsync(harness, "owner@example.com", Password, client: "10.0.0.1")).Succeeded);
    }

    [Fact]
    public async Task An_address_is_stored_normalized_and_signs_in_however_it_is_typed()
    {
        await using var harness = await ClaimedAsync();

        Assert.True((await RegisterAsync(harness, "  Mixed.Case@Example.COM ")).Succeeded);

        Assert.Equal("mixed.case@example.com", (await harness.UserAsync("mixed.case@example.com"))!.Email);
        Assert.True((await SignInAsync(harness, "MIXED.case@example.com", Password)).Succeeded);
        Assert.Equal(AuthError.DuplicateAccount, (await RegisterAsync(harness, "mixed.case@EXAMPLE.com")).Error);
    }

    /// <summary>A harness whose instance is already claimed, with one ordinary account to work against.</summary>
    private static async Task<AuthHarness> ClaimedAsync(Action<AuthOptions>? configure = null)
    {
        var harness = new AuthHarness(configure);
        await harness.StartAsync();

        var owner = await RegisterAsync(
            harness, "owner@example.com", firstRunToken: AuthHarness.FirstRunTokenValue);
        Assert.True(owner.Succeeded, $"harness setup failed: {owner.Error} {owner.Message}");

        return harness;
    }

    private static async Task<AuthResult> RegisterAsync(
        AuthHarness harness, string email, string password = Password, string? firstRunToken = null)
    {
        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<TestUser>>();
        var outcome = await accounts.RegisterAsync(email, password, firstRunToken, client: null);
        return outcome.Result;
    }

    private static async Task<AuthResult> SignInAsync(
        AuthHarness harness, string email, string password, string? client = null)
    {
        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<TestUser>>();
        var outcome = await accounts.ValidateAsync(email, password, client);
        return outcome.Result;
    }
}
