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
