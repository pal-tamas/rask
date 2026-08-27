namespace Rask.Server.Http;

/// <summary>
///     Removes the live-runtime <c>&lt;script&gt;</c> from a document that is being served without a
///     session behind it.
/// </summary>
/// <remarks>
///     <para>
///         The serializer emits the tag unconditionally, and deliberately so: a WebSocket full-HTML
///         frame goes through the same serializer, and the client morphs those frames onto
///         <c>document.documentElement</c>. Make the serializer emit a placeholder instead and that
///         morph would replace the running runtime's own <c>&lt;script&gt;</c> element with a
///         comment — deleting the runtime out from under the page it is driving.
///     </para>
///     <para>
///         So the tag is always emitted and removed here instead, on the one branch where it is
///         provably safe: a static response has no session, so nothing will ever diff against this
///         document, and frame offsets, path indices and the render baseline are all irrelevant to
///         it.
///     </para>
/// </remarks>
internal static class RuntimeScriptSplice
{
    /// <summary>
    ///     Returns <paramref name="html" /> with the runtime tag removed, or <c>null</c> when the
    ///     tag is not exactly where it is expected to be.
    /// </summary>
    /// <remarks>
    ///     Failing closed is the point. A <c>null</c> tells the caller to treat the page as
    ///     interactive — keeping its session and its <c>no-store</c> — because the alternative,
    ///     serving a document that may still contain a session-bearing script as though it were a
    ///     cacheable static page, is the one outcome worth refusing outright.
    /// </remarks>
    internal static string? TryRemove(string html, string pathBase)
    {
        var tag = ServerRuntimeScriptTag(pathBase);

        // The tag is the last child of <body> by construction, so search from the end: it is both
        // the cheapest place to find it and the only place it is legitimate.
        var at = html.LastIndexOf(tag, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        // It must sit immediately before </body>. Anything else means the document is not the shape
        // this was written against, and guessing would be worse than declining.
        var after = html.AsSpan(at + tag.Length);
        if (!after.TrimStart().StartsWith("</body>", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return string.Concat(html.AsSpan(0, at), html.AsSpan(at + tag.Length));
    }

    private static string ServerRuntimeScriptTag(string pathBase) =>
        RaskEndpointExtensions.RuntimeScriptTag(pathBase);
}
