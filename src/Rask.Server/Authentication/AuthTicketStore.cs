using System.Collections.Concurrent;
using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Server.Authentication;

internal interface IAuthTicketStore
{
    string Issue(AuthAction action, ClaimsPrincipal? principal, string? scheme, string sessionId);
    bool TryRedeem(string ticketId, string sessionId, out AuthTicket ticket);
}

internal sealed record AuthTicket(
    AuthAction Action,
    ClaimsPrincipal? Principal,
    string? Scheme,
    string SessionId,
    DateTime ExpiresUtc);

internal sealed class AuthTicketStore : IAuthTicketStore
{
    // Lifetime of a one-shot sign-in/out redeem ticket. Short by design (the ticket is the authority
    // for setting the cookie). Mutable static so tests can force expiry; not a public knob.
    internal static TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, AuthTicket> _tickets = new();
    private int _opsSinceLastSweep;

    public string Issue(AuthAction action, ClaimsPrincipal? principal, string? scheme, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        var id = Guid.NewGuid().ToString("N");
        var ticket = new AuthTicket(action, principal, scheme, sessionId, DateTime.UtcNow.Add(Ttl));
        _tickets[id] = ticket;
        MaybeSweep();
        return id;
    }

    public bool TryRedeem(string ticketId, string sessionId, out AuthTicket ticket)
    {
        if (string.IsNullOrEmpty(ticketId) || string.IsNullOrEmpty(sessionId))
        {
            ticket = default!;
            return false;
        }

        if (!_tickets.TryRemove(ticketId, out var found))
        {
            ticket = default!;
            return false;
        }

        if (found.ExpiresUtc <= DateTime.UtcNow || !string.Equals(found.SessionId, sessionId, StringComparison.Ordinal))
        {
            ticket = default!;
            return false;
        }

        ticket = found;
        return true;
    }

    private void MaybeSweep()
    {
        if (Interlocked.Increment(ref _opsSinceLastSweep) < 16)
        {
            return;
        }

        Interlocked.Exchange(ref _opsSinceLastSweep, 0);
        var now = DateTime.UtcNow;
        foreach (var kv in _tickets)
        {
            if (kv.Value.ExpiresUtc <= now)
            {
                _tickets.TryRemove(kv.Key, out _);
            }
        }
    }
}
