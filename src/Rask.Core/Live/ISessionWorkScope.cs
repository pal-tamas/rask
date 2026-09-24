using System.ComponentModel;

namespace Rask.Core.Live;

/// <summary>
///     Entered around the work a live session does, so ambient state can follow a session's flow.
/// </summary>
/// <remarks>
///     <para>
///         A session's render and handler dispatch run on their own flow, not inside the DI scope the
///         session was built with. Anything that has to answer "which session is this?" without being passed
///         down — most of all which tenant the signed-in user belongs to — needs that scope made ambient for
///         the duration of the work, and this is where the host does it.
///     </para>
///     <para>
///         It is an <b>optional</b> service, resolved once per session and held, so a host that registers
///         nothing pays nothing on the render path. Both hosts enter it. Rask.Data is the implementation
///         that exists today — its tenant filter, and the save that refreshes the page's queries: on a server
///         the meta package registers it, the one assembly that sees both the web host and the data layer
///         (<c>Rask.Server</c> does not reference <c>Rask.Data</c>, and should not have to); in the browser
///         Rask.Data's own browser build does, from <c>AddRaskData&lt;TContext&gt;()</c>.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ISessionWorkScope
{
    /// <summary>Makes <paramref name="sessionServices" /> ambient until the result is disposed.</summary>
    /// <param name="sessionServices">The session's own service provider.</param>
    /// <returns>A scope that restores what was ambient before, or null when there is nothing to do.</returns>
    /// <remarks>
    ///     Nesting is expected and must be safe: a handler's dispatch renders, so this is entered again
    ///     inside itself, and each disposal has to restore exactly what it replaced.
    /// </remarks>
    IDisposable? Enter(IServiceProvider sessionServices);
}
