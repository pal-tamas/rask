using System.Security.Claims;
using Rask.Core.Authentication;
using Rask.Server.Authentication;

namespace Rask.Server.Tests.Authentication;

public class AuthTicketStoreTests
{
    private static AuthTicketStore Store() => new();

    [Fact]
    public void Issue_Then_TryRedeem_ReturnsTicket()
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
    public void TryRedeem_Twice_SecondReturnsFalse()
    {
        var store = Store();
        var id = store.Issue(AuthAction.SignIn, new ClaimsPrincipal(new ClaimsIdentity()), null, "session-1");

        Assert.True(store.TryRedeem(id, "session-1", out _));
        Assert.False(store.TryRedeem(id, "session-1", out _));
    }

    [Fact]
    public void TryRedeem_WrongSession_ReturnsFalse()
    {
        var store = Store();
        var id = store.Issue(AuthAction.SignIn, new ClaimsPrincipal(new ClaimsIdentity()), null, "session-1");

        Assert.False(store.TryRedeem(id, "session-2", out _));
        // Mismatch consumed the ticket — even the right session can't redeem it now.
        Assert.False(store.TryRedeem(id, "session-1", out _));
    }

    [Fact]
    public void TryRedeem_UnknownTicket_ReturnsFalse()
    {
        var store = Store();
        Assert.False(store.TryRedeem("no-such-id", "session-1", out _));
    }

    [Fact]
    public void TryRedeem_Expired_ReturnsFalse()
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
    public void TryRedeem_EmptyArgs_ReturnsFalse()
    {
        var store = Store();
        Assert.False(store.TryRedeem("", "session-1", out _));
        Assert.False(store.TryRedeem("ticket", "", out _));
    }

    [Fact]
    public void Issue_SignOutAction_AcceptsNullPrincipal()
    {
        var store = Store();
        var id = store.Issue(AuthAction.SignOut, null, null, "session-1");
        Assert.True(store.TryRedeem(id, "session-1", out var ticket));
        Assert.Equal(AuthAction.SignOut, ticket.Action);
        Assert.Null(ticket.Principal);
    }
}
