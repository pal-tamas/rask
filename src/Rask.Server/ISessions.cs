using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Server;

/// <summary>
///     The live sessions' kept answers about their users — what <c>AddSessionRevalidator&lt;T&gt;().Keep(…)</c> keeps —
///     dropped for a change the app knows of and no save says: <c>sessions.Forget(user)</c>. <see cref="Sessions" /> is
///     the same with nothing injected.
/// </summary>
/// <remarks>
///     <para>
///         A user who chose another tenant, a sign-in ended by hand, a right granted in another system: every page of
///         that user open in this process — every tab, whatever principal it holds — asks the app's
///         <see cref="ISessionRevalidator" /> again at its next event, instead of acting on the kept answer for up to
///         <c>.Keep(…)</c>.
///     </para>
///     <para>
///         Call it AFTER the change is made. An answer the revalidator was giving as it is called is handed on and not
///         kept, so a forget racing a question always wins. It costs one dictionary removal; other users keep theirs.
///         It reaches this process only: another instance sees the change when its kept answer runs out.
///     </para>
/// </remarks>
public interface ISessions
{
    /// <summary>Drops every kept answer about the user with this id: their pages all ask again at their next event.</summary>
    /// <param name="userId">
    ///     The user's id as the principal carries it: its <see cref="ClaimTypes.NameIdentifier" /> claim, or its
    ///     <see cref="ClaimTypes.Name" /> when it has none.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="userId" /> is empty.</exception>
    void Forget(string userId);

    /// <summary>
    ///     Drops every kept answer about <paramref name="user" />'s user — not only those about this principal: their
    ///     pages all ask again at their next event. <c>sessions.Forget(http.User)</c>.
    /// </summary>
    /// <param name="user">A principal of the user, as a request or a session carries it. One with no id forgets nothing.</param>
    void Forget(ClaimsPrincipal user);
}
