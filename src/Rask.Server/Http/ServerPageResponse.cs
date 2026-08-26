using Rask.Core.Http;

namespace Rask.Server.Http;

/// <summary>
///     Which phase of a session's life a <see cref="ServerPageResponse" /> is being touched in.
///     Only <see cref="Initial" /> can still shape the response.
/// </summary>
internal enum PageResponsePhase
{
    /// <summary>Outside any server render — an event handler, a background render, a bare unit test.</summary>
    None,

    /// <summary>The initial GET's render walk. The response has not started; everything works.</summary>
    Initial,
}

/// <summary>
///     Server implementation of <see cref="IPageResponse" />: a state bag the GET handler reads
///     once the render has finished.
/// </summary>
/// <remarks>
///     Deliberately holds no <c>HttpContext</c>. It lives in the session scope, which outlives the
///     request whenever the page keeps a live session, so a captured context would be a use-after-
///     request waiting to happen. The handler owns applying what is recorded here.
/// </remarks>
internal sealed class ServerPageResponse : IPageResponse
{
    /// <summary>Set by the GET handler around the initial render; <see cref="PageResponsePhase.None" /> otherwise.</summary>
    internal PageResponsePhase Phase { get; set; }

    /// <summary>The status the page asked for, if any.</summary>
    internal int? Status { get; private set; }

    public void SetStatus(int statusCode)
    {
        RequireInitialRender(nameof(SetStatus));
        if (statusCode is < 200 or > 599)
        {
            throw new ArgumentOutOfRangeException(
                nameof(statusCode),
                statusCode,
                "A page's status must be in the 200–599 range. A 1xx response is informational and " +
                "cannot carry the page's body.");
        }

        Status = statusCode;
    }

    private void RequireInitialRender(string member)
    {
        if (Phase == PageResponsePhase.Initial)
        {
            return;
        }

        throw new InvalidOperationException(
            $"IPageResponse.{member} can only run during the initial server render of a page " +
            "(Render, OnMount or OnMountAsync). By the time an event handler runs, the response " +
            "has already been sent, so there is no status left to set — the call would be silently " +
            "dropped. To change the page from a handler, use Navigator; to gate a page on load, " +
            "use [Authorize] or a route. See docs/routing.md.");
    }
}
