using Rask.Core.Live;

namespace Rask.Server.Prerender;

/// <summary>
///     Shapes a page render into the bytes a response carries.
/// </summary>
/// <remarks>
///     The one place the per-response attributes are stamped onto a render, so their order — and every
///     test asserting it — cannot differ between the paths that serve a page.
/// </remarks>
internal static class PageDocument
{
    /// <summary>
    ///     A live document: <paramref name="html" /> carrying its session id, the development attributes
    ///     when <paramref name="dev" />, and the devtools host script when there is one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>data-rask-dev</c> is the client-side gate for every dev-only frame, so it is decided by the
    ///         caller from the same predicate that decides whether to subscribe at all. Where to ask about
    ///         build status when the socket drops (#603) is read from the environment, because the only thing
    ///         that can answer is <c>rask dev</c>, the process that launched this one.
    ///     </para>
    ///     <para>
    ///         <paramref name="devToolsHostUrl" /> is a separate gate from <paramref name="dev" />: that one also
    ///         needs <c>dotnet watch</c>, and the devtools switch on for a plain <c>dotnet run</c> of a Debug build
    ///         in Development.
    ///     </para>
    /// </remarks>
    internal static string Live(string html, string sessionId, bool dev, string? devToolsHostUrl) =>
        LivePayload.InjectDevToolsScript(
            LivePayload.InjectIslandsDevAttr(
                LivePayload.InjectRootAttr(
                    html, sessionId, dev, dev ? Environment.GetEnvironmentVariable("RASK_DEV_STATUS") : null),
                dev,
                dev ? Environment.GetEnvironmentVariable("RASK_ISLANDS_DEV") : null),
            devToolsHostUrl);
}
