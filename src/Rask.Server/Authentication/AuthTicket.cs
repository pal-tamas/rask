using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Server.Authentication;

internal sealed record AuthTicket(
    AuthAction Action,
    ClaimsPrincipal? Principal,
    string? Scheme,
    string SessionId,
    DateTime ExpiresUtc,
    bool Persistent = false);
