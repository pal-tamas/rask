using System.Security.Claims;
using Rask.Core.Authentication;
using Rask.Server.Authentication;

namespace Rask.Server.Tests.Authentication;

public class AuthTicketStoreTests
{
    private static AuthTicketStore Store() => new();

    [Fact]
    public void An_issued_ticket_redeems_to_itself()
    {
        var store = Store();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "Test"));
        var id = store.Issue(AuthAction.SignIn, principal, "Test", "session-1");

        Assert.True(store.TryRedeem(id, "session-1", out var ticket));
        Assert.Equal(AuthAction.SignIn, ticket.Action);
        Assert.Same(principal, ticket.Principal);
        Assert.Equal("Test", ticket.Scheme);
        Assert.Equal("session-1", ticket.SessionId);
    }

    [Fact]
    public void A_ticket_redeems_only_once()
    {
        var store = Store();
        var id = store.Issue(AuthAction.SignIn, new ClaimsPrincipal(new ClaimsIdentity()), null, "session-1");

        Assert.True(store.TryRedeem(id, "session-1", out _));
        Assert.False(store.TryRedeem(id, "session-1", out _));
    }

    [Fact]
    public void A_ticket_offered_for_the_wrong_session_does_not_redeem()
    {
        var store = Store();
        var id = store.Issue(AuthAction.SignIn, new ClaimsPrincipal(new ClaimsIdentity()), null, "session-1");

        Assert.False(store.TryRedeem(id, "session-2", out _));
        // Mismatch consumed the ticket — even the right session can't redeem it now.
        Assert.False(store.TryRedeem(id, "session-1", out _));
    }

    [Fact]
    public void An_unknown_ticket_does_not_redeem()
    {
        var store = Store();

        Assert.False(store.TryRedeem("no-such-id", "session-1", out _));
    }

    [Fact]
    public void An_expired_ticket_does_not_redeem()
    {
        // A negative TTL makes every issued ticket already expired by the time it's redeemed.
        var prev = AuthTicketStore.Ttl;
        AuthTicketStore.Ttl = TimeSpan.FromMilliseconds(-1);
        try
        {
            var store = Store();
            var id = store.Issue(AuthAction.SignIn, new ClaimsPrincipal(new ClaimsIdentity()), null, "session-1");

            Assert.False(store.TryRedeem(id, "session-1", out _));
        }
        finally
        {
            AuthTicketStore.Ttl = prev;
        }
    }

    [Fact]
    public void Empty_arguments_do_not_redeem()
    {
        var store = Store();

        Assert.False(store.TryRedeem("", "session-1", out _));
        Assert.False(store.TryRedeem("ticket", "", out _));
    }

    [Fact]
    public void A_sign_out_ticket_accepts_a_null_principal()
    {
        var store = Store();

        var id = store.Issue(AuthAction.SignOut, null, null, "session-1");

        Assert.True(store.TryRedeem(id, "session-1", out var ticket));
        Assert.Equal(AuthAction.SignOut, ticket.Action);
        Assert.Null(ticket.Principal);
    }
}
