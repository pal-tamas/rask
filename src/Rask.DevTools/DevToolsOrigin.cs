namespace Rask.DevTools;

/// <summary>
///     Whether a page's origin is this machine: the browser host's half of the rule the Server host applies to the panel.
/// </summary>
/// <remarks>
///     <para>
///         A WASM app has no server to ask about the environment — a static host serves the same files in development and
///         in production — so the browser decides from what it can see. A Debug build is the first condition, and the
///         loader already checks it; a page served from this machine is the second. A Debug bundle someone put on a real
///         host does not switch the tools on for its visitors.
///     </para>
///     <para>
///         <see cref="Uri.IsLoopback" /> covers <c>localhost</c> and the loopback addresses of both families. Anything that
///         does not parse as an absolute URL is not local.
///     </para>
/// </remarks>
internal static class DevToolsOrigin
{
    internal static bool IsLoopback(string? origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.IsLoopback;
}
