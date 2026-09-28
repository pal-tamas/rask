using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Server.Authentication;

internal interface IAuthTicketStore
{
    string Issue(AuthAction action, ClaimsPrincipal? principal, string? scheme, string sessionId, bool persistent = false);
    bool TryRedeem(string ticketId, string sessionId, out AuthTicket ticket);
}
