using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Server.Authentication;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Authentication;

/// <summary>
///     An <see cref="ISessionRevalidator" /> of the app's own on a live page: its one method says who its own user acts as
///     now, and every event of the page acts for the user as the store has them.
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
            services.AddSingleton<ISessionRevalidator, AccountCheck>();
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
    public async Task A_role_given_in_the_store_is_held_at_the_next_event()
    {
        var accounts = new Accounts();
        using var host = M2SignedInPage.CreateHost(services =>
        {
            services.AddSingleton(accounts);
            services.AddSingleton<ISessionRevalidator, AccountCheck>();
        });
        var (ws, sessionId, handlerId) = await M2SignedInPage.AttachAsync(host);
        using var socket = ws;

        accounts.Alice = accounts.Alice with { Role = "editor" };
        await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
        await ws.SettledAsync();

        Assert.True(UserOf(host, sessionId).IsInRole("editor"));
    }

    private static ClaimsPrincipal UserOf(RaskTestHost host, string sessionId) =>
        host.Store.Get(sessionId)!.Services.GetRequiredService<SessionUserProvider>().Current;

    /// <summary>What the app's store holds about alice.</summary>
    internal sealed class Accounts
    {
        public Account Alice { get; set; } = new(Disabled: false, Role: null);

        public Account? Find(string userId) => userId == "alice" ? Alice : null;
    }

    internal sealed record Account(bool Disabled, string? Role);

    // The app's own revalidator, one method: a disabled account has ended, a role the principal lacks is added to it.
    private sealed class AccountCheck(Accounts accounts) : ISessionRevalidator
    {
        public ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
            new(accounts.Find(principal.FindFirstValue(ClaimTypes.NameIdentifier)!) switch
            {
                null or { Disabled: true } => null,
                { Role: null } => principal,
                { Role: var role } when principal.IsInRole(role) => principal,
                { Role: var role } => new ClaimsPrincipal(new ClaimsIdentity(
                    principal.Claims.Append(new Claim(ClaimTypes.Role, role)), "TestCookie")),
            });
    }
}
