using System.Text;

namespace Rask.Wasm;

/// <summary>
///     Splices a prerendered document into the published boot shell.
/// </summary>
/// <remarks>
///     <para>
///         Prerendering writes into the published <c>wwwroot</c>, where <c>index.html</c> is already
///         the boot shell the WebAssembly SDK has just filled in — the fingerprinted import map, the
///         subresource-integrity-pinned preload, the <c>&lt;base href&gt;</c>, and
///         <c>&lt;script src="main.js"&gt;</c>. Writing the rendered document over it would take all of
///         that with it, and the bundle would never boot: the page would carry real markup and no way
///         to become interactive.
///     </para>
///     <para>
///         The framework cannot re-emit those tags instead. On the Server the boot script comes from an
///         <c>IRaskRuntimeScript</c> registration, but the WASM host deliberately registers none — the
///         runtime boots from the page shell — and the import map's fingerprints and hashes are minted
///         by the SDK per publish, so managed code has nothing to reproduce them from. The shell is the
///         only place they exist. So it is kept, and the render is spliced into it.
///     </para>
///     <para>
///         What the runtime then does with the result is unchanged: it morphs its first real render onto
///         the document, exactly as it already morphs over the boot spinner. The prerendered body is the
///         placeholder that morph was always designed to replace — it is simply a useful one.
///     </para>
/// </remarks>
internal static class PrerenderShell
{
    /// <summary>
    ///     Returns <paramref name="shell" /> carrying <paramref name="document" />'s head contributions
    ///     and body, or <paramref name="document" /> unchanged when the two cannot be spliced.
    /// </summary>
    /// <remarks>
    ///     Falling back to the whole document rather than throwing is deliberate: a caller driving its
    ///     own pass may have no shell at all, and a prerendered page with no boot script is still worth
    ///     more than a failed publish. The callers that DO have a shell are the ones that would notice.
    /// </remarks>
    internal static string Merge(string shell, string document)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(document);

        if (!TryFindElement(shell, "head", out var shellHead)
            || !TryFindElement(shell, "body", out var shellBody)
            || !TryFindElement(document, "body", out var documentBody))
        {
            return document;
        }

        // The document's head is optional in a way the body is not: a root component contributing no
        // head assets still renders <head></head>, but a caller's hand-built component might not.
        var documentHead = TryFindElement(document, "head", out var found) ? found : default;

        var builder = new StringBuilder(shell.Length + document.Length);

        // --- everything up to the shell's </head>, minus the tags the document is about to supply ---
        var shellHeadInner = shell[shellHead.InnerStart..shellHead.InnerEnd];
        var documentHeadInner = documentHead.InnerEnd > documentHead.InnerStart
            ? document[documentHead.InnerStart..documentHead.InnerEnd]
            : string.Empty;

        builder.Append(shell, 0, shellHead.InnerStart);
        builder.Append(HasTitle(documentHeadInner) ? RemoveTitle(shellHeadInner) : shellHeadInner);
        builder.Append(StripShellOwnedTags(documentHeadInner));
        builder.Append(shell, shellHead.InnerEnd, shellBody.InnerStart - shellHead.InnerEnd);

        // --- the rendered body, then the shell's own scripts ---
        // The shell's body is a boot placeholder plus the script that boots the bundle. The placeholder
        // is exactly what the render replaces; the scripts are the whole reason for keeping the shell,
        // so they are carried over verbatim and in order.
        builder.Append(document, documentBody.InnerStart, documentBody.InnerEnd - documentBody.InnerStart);
        AppendScripts(builder, shell.AsSpan(shellBody.InnerStart, shellBody.InnerEnd - shellBody.InnerStart));

        builder.Append(shell, shellBody.InnerEnd, shell.Length - shellBody.InnerEnd);

        return builder.ToString();
    }

    /// <summary>
    ///     Tags the shell owns outright, dropped from the document's head so the merge cannot end up
    ///     with two of them.
    /// </summary>
    /// <remarks>
    ///     <c>&lt;base&gt;</c> is the load-bearing one: the shell's is what a sub-path publish rewrites,
    ///     and a second one later in the head would silently win for every relative URL on the page.
    ///     <c>&lt;meta charset&gt;</c> has to be the first thing in the head to count at all, so the
    ///     document's copy is redundant wherever it lands.
    /// </remarks>
    private static string StripShellOwnedTags(string headInner)
    {
        var result = RemoveElements(headInner, "base", selfClosing: true);
        return RemoveCharsetMeta(result);
    }

    private static bool HasTitle(string headInner) =>
        headInner.Contains("<title", StringComparison.OrdinalIgnoreCase);

    private static string RemoveTitle(string headInner) =>
        RemoveElements(headInner, "title", selfClosing: false);

    /// <summary>
    ///     Copies every <c>&lt;script&gt;</c> element out of <paramref name="bodyInner" />, in order.
    /// </summary>
    private static void AppendScripts(StringBuilder builder, ReadOnlySpan<char> bodyInner)
    {
        var cursor = 0;
        while (cursor < bodyInner.Length)
        {
            var open = IndexOfTag(bodyInner[cursor..], "script");
            if (open < 0)
            {
                return;
            }

            open += cursor;
            var close = bodyInner[open..].IndexOf("</script>", StringComparison.OrdinalIgnoreCase);
            if (close < 0)
            {
                return;
            }

            close += open + "</script>".Length;
            builder.Append('\n').Append(bodyInner[open..close]);
            cursor = close;
        }
    }

    /// <summary>
    ///     Removes every occurrence of an element, with its content when it has an end tag.
    /// </summary>
    private static string RemoveElements(string html, string name, bool selfClosing)
    {
        var result = html;
        while (true)
        {
            var open = IndexOfTag(result, name);
            if (open < 0)
            {
                return result;
            }

            int end;
            if (selfClosing)
            {
                var gt = result.AsSpan(open).IndexOf('>');
                if (gt < 0)
                {
                    return result;
                }

                end = open + gt + 1;
            }
            else
            {
                var closeTag = $"</{name}>";
                var close = result.IndexOf(closeTag, open, StringComparison.OrdinalIgnoreCase);
                if (close < 0)
                {
                    return result;
                }

                end = close + closeTag.Length;
            }

            result = result.Remove(open, end - open);
        }
    }

    /// <summary>
    ///     Removes a <c>&lt;meta charset&gt;</c> declaration, in either of the two spellings.
    /// </summary>
    private static string RemoveCharsetMeta(string html)
    {
        var cursor = 0;
        while (true)
        {
            var open = IndexOfTag(html.AsSpan(cursor), "meta");
            if (open < 0)
            {
                return html;
            }

            open += cursor;
            var gt = html.AsSpan(open).IndexOf('>');
            if (gt < 0)
            {
                return html;
            }

            var end = open + gt + 1;
            var tag = html[open..end];
            if (tag.Contains("charset", StringComparison.OrdinalIgnoreCase))
            {
                return html.Remove(open, end - open);
            }

            cursor = end;
        }
    }

    /// <summary>
    ///     Finds <c>&lt;name</c> as a real tag rather than as a prefix of a longer one.
    /// </summary>
    /// <remarks>
    ///     Without the delimiter check, looking for <c>&lt;base</c> also matches a hypothetical
    ///     <c>&lt;basefont&gt;</c>, and looking for <c>&lt;script</c> would match <c>&lt;scripts&gt;</c>.
    ///     A tag name ends at whitespace, <c>/</c>, or <c>&gt;</c>.
    /// </remarks>
    private static int IndexOfTag(ReadOnlySpan<char> html, string name)
    {
        var cursor = 0;
        while (cursor < html.Length)
        {
            var hit = html[cursor..].IndexOf($"<{name}", StringComparison.OrdinalIgnoreCase);
            if (hit < 0)
            {
                return -1;
            }

            hit += cursor;
            var after = hit + 1 + name.Length;
            if (after >= html.Length)
            {
                return -1;
            }

            var next = html[after];
            if (char.IsWhiteSpace(next) || next is '>' or '/')
            {
                return hit;
            }

            cursor = after;
        }

        return -1;
    }

    /// <summary>
    ///     Where an element's content starts and ends, exclusive of its own tags.
    /// </summary>
    private readonly record struct ElementSpan(int InnerStart, int InnerEnd);

    private static bool TryFindElement(string html, string name, out ElementSpan span)
    {
        span = default;

        var open = IndexOfTag(html, name);
        if (open < 0)
        {
            return false;
        }

        var gt = html.AsSpan(open).IndexOf('>');
        if (gt < 0)
        {
            return false;
        }

        var innerStart = open + gt + 1;

        // The LAST end tag, not the first: a </body> can legitimately appear inside a <script> string
        // earlier in the document, and closing the body there would drop everything after it.
        var closeTag = $"</{name}>";
        var innerEnd = html.LastIndexOf(closeTag, StringComparison.OrdinalIgnoreCase);
        if (innerEnd < innerStart)
        {
            return false;
        }

        span = new ElementSpan(innerStart, innerEnd);
        return true;
    }
}
