using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;

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
    public async Task Too_many_wrong_passwords_lock_the_account()
    {
        await using var harness = await ClaimedAsync(o => o.MaxFailedAccessAttempts = 3);

        for (var i = 0; i < 3; i++)
        {
            await SignInAsync(harness, "owner@example.com", "WrongPassword1");
        }

        // Locked out now, so even the right password is refused — that is the point of a lockout.
        var result = await SignInAsync(harness, "owner@example.com", Password);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.LockedOut, result.Error);
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
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<RaskUser>>();
        var outcome = await accounts.RegisterAsync(email, password, firstRunToken);
        return outcome.Result;
    }

    private static async Task<AuthResult> SignInAsync(AuthHarness harness, string email, string password)
    {
        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<RaskUser>>();
        var outcome = await accounts.ValidateAsync(email, password);
        return outcome.Result;
    }
}
