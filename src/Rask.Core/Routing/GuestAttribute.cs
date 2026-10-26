namespace Rask.Core.Routing;

/// <summary>
///     Makes a page one only a reader who is not signed in opens — a sign-in or a registration page. A reader who is
///     signed in is sent to <see cref="Then" /> instead, before the page is built: a <c>302</c> on a first request, and
///     in an open session a navigation that replaces the address.
/// </summary>
/// <remarks>
///     Asked where <c>[Authorize]</c> is, of the same reader: the request's user on a first request, and the
///     session's user as last revalidated in an open session — so a sign-in that has ended is a guest again. On a
///     layout it covers every page under it.
/// </remarks>
/// <example>
///     <code>
///     [Guest(Then = "/app")]
///     [Route("/login")]
///     public sealed partial class LoginPage : Component { … }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GuestAttribute : Attribute
{
    /// <summary>
    ///     The page of this app a signed-in reader is sent to: a path written from the app's root, as
    ///     <c>Go.To</c> takes one. <c>"/"</c> by default.
    /// </summary>
    public string Then { get; set; } = "/";
}
