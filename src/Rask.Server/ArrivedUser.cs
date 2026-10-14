using System.Security.Claims;

namespace Rask.Server;

/// <summary>A credential a connection or a post brought while the session was busy, waiting to be adopted.</summary>
/// <param name="Carried">The principal as the request authenticated it.</param>
/// <param name="Answer">What the revalidator made of it: the principal to act under, or null when its sign-in has ended.</param>
/// <param name="Held">Whom the session held as it arrived, and was let in against.</param>
internal sealed record ArrivedUser(ClaimsPrincipal Carried, ClaimsPrincipal? Answer, ClaimsPrincipal Held);
