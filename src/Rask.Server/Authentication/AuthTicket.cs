using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Server.Authentication;

internal sealed record AuthTicket(
    AuthAction Action,
    ClaimsPrincipal? Principal,
    string? Scheme,
    string SessionId,
    DateTime ExpiresUtc,
    bool Persistent = false,
    string? Token = null,
    TenantPick? TenantPick = null)
{
    // Never the token, the principal or the session id: a ticket printed into a log must not hand the session over.
    public override string ToString() => $"AuthTicket {{ Action = {Action}, ExpiresUtc = {ExpiresUtc:O} }}";
}
