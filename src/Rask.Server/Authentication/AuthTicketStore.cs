using System.Collections.Concurrent;
using System.Security.Claims;
using Rask.Core;
using Rask.Core.Authentication;

namespace Rask.Server.Authentication;

/// <summary>
///     The single-use tickets a live handler's sign-in or sign-out is handed to the browser with. A ticket is taken
///     once — redeemed, refused for another session, or expired, it is gone — and lives 30 seconds.
/// </summary>
/// <param name="clock">The clock a ticket's life is measured on; the system clock when the host registers none.</param>
internal sealed class AuthTicketStore(TimeProvider? clock = null) : IAuthTicketStore
{
    // Short by design: the ticket is the authority for setting the cookie, and the browser redeems it at once.
    internal static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, AuthTicket> _tickets = new(StringComparer.Ordinal);
    private int _opsSinceLastSweep;

    public string Issue(
        AuthAction action,
        ClaimsPrincipal? principal,
        string? scheme,
        string sessionId,
        bool persistent = false,
        string? token = null,
        TenantPick? tenantPick = null)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        var id = SecureToken.Create();
        var ticket = new AuthTicket(action, principal, scheme, sessionId, Now().Add(Ttl), persistent, token, tenantPick);
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

        if (found.ExpiresUtc <= Now() || !string.Equals(found.SessionId, sessionId, StringComparison.Ordinal))
        {
            ticket = default!;
            return false;
        }

        ticket = found;
        return true;
    }

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;

    private void MaybeSweep()
    {
        if (Interlocked.Increment(ref _opsSinceLastSweep) < 16)
        {
            return;
        }

        Interlocked.Exchange(ref _opsSinceLastSweep, 0);
        var now = Now();
        foreach (var kv in _tickets)
        {
            if (kv.Value.ExpiresUtc <= now)
            {
                _tickets.TryRemove(kv.Key, out _);
            }
        }
    }
}
