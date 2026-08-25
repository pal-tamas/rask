using Microsoft.AspNetCore.Http;

namespace Rask.Server;

/// <summary>
///     How a native shell tells a Rask server that the page it is asking for will be displayed inside
///     native chrome, so the portable bars stop emitting markup and describe themselves instead.
/// </summary>
/// <remarks>
///     <para>
///         A <b>request header</b>, not a query string and not a WebSocket message. The query string would
///         end up in the app's own URLs and in its route matching; the WebSocket hello arrives long after
///         the document has been rendered and sent, which is exactly too late — the bars would ship as
///         HTML, paint, and then vanish.
///     </para>
///     <para>
///         Both platform WebViews can set request headers on the load they initiate, so the head can say
///         this before the server renders a single element.
///     </para>
/// </remarks>
internal static class RaskShellHeader
{
    /// <summary>The header a native head sends. Named for the framework so nothing else collides with it.</summary>
    public const string Name = "X-Rask-Shell";

    /// <summary>The one value that means anything today.</summary>
    public const string NativeValue = "native";

    /// <summary>
    ///     Whether this request came from a native shell. Unknown values read as "no": a header this server
    ///     does not understand must not change how it renders.
    /// </summary>
    public static bool IsNativeShell(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Headers.TryGetValue(Name, out var values)
            && values.Count > 0
            && string.Equals(values[0], NativeValue, StringComparison.OrdinalIgnoreCase);
    }
}
