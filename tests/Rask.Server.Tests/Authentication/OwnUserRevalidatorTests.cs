using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Authentication;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Authentication;

/// <summary>
///     <c>AddSessionRevalidator&lt;T&gt;()</c> on a live page: the app says how its own user is loaded and what
///     principal they act under, and every event of the page acts for the user as the store has them.
/// </summary>
public sealed class OwnUserRevalidatorTests
{
    [Fact]
    public async Task A_user_disabled_in_the_store_is_signed_out_before_their_next_handler_runs()
    {
        var accounts = new Accounts();
        using var host = M2SignedInPage.CreateHost(services =>
        {
            services.AddSingleton(accounts);
            services.AddSessionRevalidator<AccountCheck>();
        });
        var counter = host.Server.Services.GetRequiredService<M2Counter>();
        var (ws, sessionId, handlerId) = await M2SignedInPage.AttachAsync(host);
        using var socket = ws;

        accounts.Alice = accounts.Alice with { Disabled = true };
        await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
        var afterDisabled = await ws.ReceiveTextAsync();

        Assert.Equal(0, counter.Count);
        Assert.Contains("/login", afterDisabled);
        Assert.NotEqual(true, UserOf(host, sessionId).Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task A_role_given_in_the_store_is_held_at_the_first_event_after_the_kept_user_runs_out()
    {
        var accounts = new Accounts();
        using var host = M2SignedInPage.CreateHost(services =>
        {
            services.AddSingleton(accounts);
            services.AddSessionRevalidator<AccountCheck>().Keep(5.Seconds);
        });
        var clock = (StoppedClock)host.Server.Services.GetRequiredService<TimeProvider>();
        var (ws, sessionId, handlerId) = await M2SignedInPage.AttachAsync(host);
        using var socket = ws;

        accounts.Alice = accounts.Alice with { Role = "editor" };
        clock.Advance(6.Seconds);
        await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
        await ws.SettledAsync();

        Assert.True(UserOf(host, sessionId).IsInRole("editor"));
    }

    [Fact]
    public async Task Events_within_the_kept_time_do_not_read_the_store_again()
    {
        var accounts = new Accounts();
        using var host = M2SignedInPage.CreateHost(services =>
        {
            services.AddSingleton(accounts);
            services.AddSessionRevalidator<AccountCheck>().Keep(5.Seconds);
        });
        var counter = host.Server.Services.GetRequiredService<M2Counter>();
        var (ws, _, handlerId) = await M2SignedInPage.AttachAsync(host);
        using var socket = ws;
        var loadsAtAttach = accounts.Loads;

        await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
        await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
        await WaitFor.True(() => counter.Count == 2, "both handlers run");

        Assert.Equal(1, loadsAtAttach);
        Assert.Equal(1, accounts.Loads);
    }

    private static ClaimsPrincipal UserOf(RaskTestHost host, string sessionId) =>
        host.Store.Get(sessionId)!.Services.GetRequiredService<SessionUserProvider>().Current;

    /// <summary>What the app's store holds about alice, and how many times it was read.</summary>
    internal sealed class Accounts
    {
        private int _loads;

        public Account Alice { get; set; } = new(Disabled: false, Role: null);

        public int Loads => Volatile.Read(ref _loads);

        public Account? Find(string userId)
        {
            Interlocked.Increment(ref _loads);
            return userId == "alice" ? Alice : null;
        }
    }

    internal sealed record Account(bool Disabled, string? Role);

    // The app's own revalidator: a disabled account has ended, a role the principal lacks is added to it.
    private sealed class AccountCheck(Accounts accounts) : SessionRevalidator<Account>
    {
        protected override Task<Account?> Load(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(accounts.Find(userId));

        protected override ClaimsPrincipal? Principal(Account user, ClaimsPrincipal principal) =>
            user.Disabled ? null
            : user.Role is null || principal.IsInRole(user.Role) ? principal
            : new ClaimsPrincipal(new ClaimsIdentity(
                principal.Claims.Append(new Claim(ClaimTypes.Role, user.Role)), "TestCookie"));
    }
}
