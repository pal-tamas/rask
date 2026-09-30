using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Wire;

namespace Rask.Auth.Tests;

/// <summary>
///     The first account to register becomes the administrator, and only the first.
/// </summary>
[Collection(AuthDbCollection.Name)]
public sealed class FirstUserIsAdminTests
{
    private const string Password = "Password1";

    [Fact]
    public async Task The_first_account_to_register_is_an_admin()
    {
        await using var harness = new AuthHarness();
        await harness.StartAsync();

        var result = await RegisterAsync(harness, "first@example.com", AuthHarness.FirstRunTokenValue);

        Assert.True(result.Succeeded, $"registration failed: {result.Error} {result.Message}");
        Assert.Contains(RaskRoles.Admin, await harness.RolesOfAsync("first@example.com"));
    }

    [Fact]
    public async Task Every_account_after_the_first_is_an_ordinary_user()
    {
        await using var harness = new AuthHarness();
        await harness.StartAsync();

        await RegisterAsync(harness, "first@example.com", AuthHarness.FirstRunTokenValue);
        var second = await RegisterAsync(harness, "second@example.com");

        Assert.True(second.Succeeded, $"registration failed: {second.Error} {second.Message}");

        var roles = await harness.RolesOfAsync("second@example.com");
        Assert.Contains(RaskRoles.User, roles);
        Assert.DoesNotContain(RaskRoles.Admin, roles);
    }

    [Fact]
    public async Task The_first_registration_needs_the_token()
    {
        await using var harness = new AuthHarness();
        await harness.StartAsync();

        var result = await RegisterAsync(harness, "first@example.com");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.FirstRunTokenRequired, result.Error);
        Assert.Equal(0, await harness.UserCountAsync());
    }

    [Fact]
    public async Task A_missing_token_and_a_wrong_token_are_the_same_answer()
    {
        await using var harness = new AuthHarness();
        await harness.StartAsync();

        var missing = await RegisterAsync(harness, "a@example.com");
        var wrong = await RegisterAsync(harness, "b@example.com", "not-the-token");

        // Same code and no detail either way: a caller must not be able to tell how close it came.
        Assert.Equal(missing.Error, wrong.Error);
        Assert.Equal(missing.Message, wrong.Message);
        Assert.Null(wrong.Message);
    }

    [Fact]
    public async Task Once_claimed_the_token_is_no_longer_asked_for()
    {
        await using var harness = new AuthHarness();
        await harness.StartAsync();

        Assert.True(harness.Token.IsPending);

        await RegisterAsync(harness, "first@example.com", AuthHarness.FirstRunTokenValue);

        Assert.False(harness.Token.IsPending);

        var later = await RegisterAsync(harness, "later@example.com");
        Assert.True(later.Succeeded, $"registration failed: {later.Error} {later.Message}");
    }

    [Fact]
    public async Task A_restart_of_a_claimed_app_issues_no_token()
    {
        await using var harness = new AuthHarness();
        await harness.StartAsync();
        await RegisterAsync(harness, "first@example.com", AuthHarness.FirstRunTokenValue);

        // A second harness over the SAME file is this app starting again. A new file would only prove
        // that a different app issues its own token.
        await using var restarted = new AuthHarness(dbPath: harness.DbPath);
        await restarted.StartAsync();

        Assert.False(
            restarted.Token.IsPending,
            "restarting an app that already has accounts must not re-open the claim window");
    }

    /// <summary>
    ///     Several registrations arriving together still produce one admin, end to end.
    /// </summary>
    /// <remarks>
    ///     This covers the wiring — that <c>RegisterAsync</c> asks the claim store at all, and hands the
    ///     losers the ordinary role — but it is <b>not</b> what pins the single-winner guarantee. Creating
    ///     an account is slow enough that the first racer commits its claim before the second one reads,
    ///     so a naive read-then-write passes this test too; that was verified by deliberately breaking the
    ///     store and watching this stay green.
    ///     <see cref="InstanceClaimStoreTests.Exactly_one_of_many_simultaneous_claims_wins" /> is the test
    ///     with teeth.
    /// </remarks>
    [Fact]
    public async Task Concurrent_first_registrations_produce_exactly_one_admin()
    {
        await using var harness = new AuthHarness();
        await harness.StartAsync();

        const int racers = 6;
        using var gate = new Barrier(racers);

        var results = await Task.WhenAll(Enumerable.Range(0, racers).Select(i => Task.Run(async () =>
        {
            using var scope = harness.NewScope();
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService<TestUser>>();

            // Line every racer up so they hit the claim at the same moment.
            gate.SignalAndWait();

            return await accounts.RegisterAsync(
                $"racer{i}@example.com", Password, AuthHarness.FirstRunTokenValue, client: null);
        })));

        Assert.All(
            results,
            r => Assert.True(r.Result.Succeeded, $"a racer failed: {r.Result.Error} {r.Result.Message}"));

        var admins = 0;

        for (var i = 0; i < racers; i++)
        {
            if ((await harness.RolesOfAsync($"racer{i}@example.com")).Contains(RaskRoles.Admin))
            {
                admins++;
            }
        }

        Assert.Equal(1, admins);
        Assert.Equal(racers, await harness.UserCountAsync());
    }

    /// <summary>
    ///     The interleaving #1143 caught once in six racers, made certain: another registration wins the claim and clears
    ///     the token in the instant after this one read "unclaimed". The token is still what this racer was given, so it
    ///     must register rather than be told a token is required.
    /// </summary>
    [Fact]
    public async Task A_racer_whose_token_is_cleared_mid_check_still_registers()
    {
        ClaimedJustAfterTheRead? claims = null;
        await using var harness = new AuthHarness(extraServices: s => s.Replace(ServiceDescriptor.Singleton<IInstanceClaimStore>(sp =>
            claims = new ClaimedJustAfterTheRead(
                new InstanceClaimStore<AuthDbContext>(
                    sp.GetRequiredService<IDbContextFactory<AuthDbContext>>(), sp.GetService<TimeProvider>() ?? TimeProvider.System),
                sp.GetRequiredService<FirstRunToken>()))));
        await harness.StartAsync();
        claims!.Armed = true;

        var result = await RegisterAsync(harness, "racer@example.com", AuthHarness.FirstRunTokenValue);

        Assert.True(result.Succeeded, $"refused: {result.Error}");
    }

    // Once armed, reads the real answer, then lets "the winner" finish — claim saved, token cleared — before handing it
    // back. Armed only after start-up, whose own IsClaimedAsync is what issues the token in the first place.
    private sealed class ClaimedJustAfterTheRead(IInstanceClaimStore inner, FirstRunToken token) : IInstanceClaimStore
    {
        public bool Armed { get; set; }

        public async Task<bool> IsClaimedAsync(CancellationToken cancellationToken = default)
        {
            var claimed = await inner.IsClaimedAsync(cancellationToken);
            if (Armed)
            {
                Armed = false;
                await inner.TryClaimAsync(Guid.NewGuid(), cancellationToken);
                token.Clear();
            }

            return claimed;
        }

        public Task<bool> TryClaimAsync(Guid userId, CancellationToken cancellationToken = default) =>
            inner.TryClaimAsync(userId, cancellationToken);
    }

    private static async Task<AuthResult> RegisterAsync(
        AuthHarness harness, string email, string? token = null)
    {
        using var scope = harness.NewScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService<TestUser>>();
        var outcome = await accounts.RegisterAsync(email, Password, token, client: null);
        return outcome.Result;
    }
}
