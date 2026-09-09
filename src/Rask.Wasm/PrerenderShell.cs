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
    ///     Marks a document whose body was spliced in by this pass, so the boot script can tell a page
    ///     that already has its content from a shell that has none.
    /// </summary>
    internal const string PrerenderedAttribute = "data-rask-prerendered";

    /// <summary>
    ///     The identity attribute the framework stamps on a keyed node, and so on every head asset a
    ///     rendered document contributes.
    /// </summary>
    private const string KeyAttribute = "data-rask-key";

    /// <summary>
    ///     Whether a document is this pass's own OUTPUT rather than a boot shell to splice into.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The shell is read from <c>index.html</c>, and the root route's own output IS
    ///         <c>index.html</c>. Publishing twice into the same directory therefore hands the second
    ///         pass the first pass's merged page as its "shell", and <see cref="Merge" /> splices the
    ///         head assets into a document that already carries every one of them. Nothing fails: the
    ///         build is green, the page renders, and each publish appends another full copy of the head
    ///         — every stylesheet, preload, meta and canonical — which only shows up if something counts
    ///         the elements. A duplicated canonical or <c>og:</c> tag is an SEO defect rather than a
    ///         cosmetic one (#1036).
    ///     </para>
    ///     <para>
    ///         Two independent tells, because neither covers the whole ground alone.
    ///         <see cref="PrerenderedAttribute" /> is stamped by every merge — but only when the shell
    ///         had an <c>&lt;html&gt;</c> tag to stamp it onto. <see cref="KeyAttribute" /> in the head
    ///         finds the framework's own keyed head assets, which a rendered page's head is full of and
    ///         a boot shell — a hand-written file the SDK only fills placeholders into — never carries.
    ///     </para>
    /// </remarks>
    internal static bool IsRendered(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var open = IndexOfTag(html, "html");
        if (open >= 0)
        {
            var gt = html.AsSpan(open).IndexOf('>');
            if (gt > 0)
            {
                var attributes = html.Substring(open + 5, gt - 5).Trim().TrimEnd('/').Trim();
                if (HasAttribute(attributes, PrerenderedAttribute))
                {
                    return true;
                }
            }
        }

        return TryFindElement(html, "head", out var head)
               && html.AsSpan(head.InnerStart, head.InnerEnd - head.InnerStart)
                   .Contains(KeyAttribute, StringComparison.OrdinalIgnoreCase);
    }

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

        builder.Append(MergeHtmlAttributes(shell[..shellHead.InnerStart], document));
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
    ///     Carries the rendered document's <c>&lt;html&gt;</c> attributes onto the shell's, keeping the
    ///     shell's value wherever both name the same attribute.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Without this, everything a root component's <c>Shell</c> override puts on
    ///         <c>&lt;html&gt;</c> is silently dropped by prerendering: the merge keeps the shell,
    ///         because the shell is what carries the import map and the boot script, and the shell's
    ///         opening tag comes from the SDK rather than from the app.
    ///     </para>
    ///     <para>
    ///         It is not a theoretical loss. The landing site sets <c>data-rask-ui</c> there to turn the
    ///         component kit's theme on, and shipped to production with every colour computing to
    ///         nothing — structurally perfect, entirely grey — because the attribute never reached the
    ///         published page. Layout survives that, which is what makes it so quiet.
    ///     </para>
    ///     <para>
    ///         The shell wins on conflict. Its <c>lang</c> and any attribute a sub-path publish rewrote
    ///         are the ones that were computed for THIS publish, and a render that disagrees is a render
    ///         that did not know about it.
    ///     </para>
    /// </remarks>
    private static string MergeHtmlAttributes(string shellPrefix, string document)
    {
        var shellOpen = IndexOfTag(shellPrefix, "html");
        if (shellOpen < 0)
        {
            return shellPrefix;
        }

        var shellGt = shellPrefix.AsSpan(shellOpen).IndexOf('>');
        if (shellGt <= 0)
        {
            return shellPrefix;
        }

        var shellAttrs = shellPrefix.Substring(shellOpen + 5, shellGt - 5).Trim().TrimEnd('/').Trim();

        var added = new StringBuilder();

        // The marker that tells the boot script this page already has its content.
        //
        // It changes what booting IS. On a shell, the runtime is the only thing between the visitor and
        // a page, so it starts the moment the module script runs. On a prerendered page the content is
        // already on screen, and starting there means the runtime monopolises the main thread BEFORE the
        // browser has taken a frame — measured on this site as a largest-contentful-paint of 37.8s whose
        // element was in the HTML all along, with 37.4s of it recorded as render delay.
        //
        // Stamped here rather than inferred by the client, because the two cases have to be told apart
        // exactly. "Is the boot spinner missing" is the same question most of the time and not always:
        // a hand-written shell need not have one, and would then defer a boot nobody is looking at
        // content during.
        if (!HasAttribute(shellAttrs, PrerenderedAttribute))
        {
            added.Append(' ').Append(PrerenderedAttribute);
        }

        var documentOpen = IndexOfTag(document, "html");
        var documentGt = documentOpen < 0 ? -1 : document.AsSpan(documentOpen).IndexOf('>');
        if (documentOpen >= 0 && documentGt > 0)
        {
            var documentAttrs = document
                .Substring(documentOpen + 5, documentGt - 5).Trim().TrimEnd('/').Trim();

            foreach (var attribute in SplitAttributes(documentAttrs))
            {
                var name = AttributeName(attribute);
                if (name.Length == 0 || HasAttribute(shellAttrs, name))
                {
                    continue;
                }

                added.Append(' ').Append(attribute);
            }
        }

        if (added.Length == 0)
        {
            return shellPrefix;
        }

        var insertAt = shellOpen + shellGt;
        return shellPrefix[..insertAt] + added + shellPrefix[insertAt..];
    }

    /// <summary>Splits an attribute list, respecting quoted values.</summary>
    private static List<string> SplitAttributes(string attributes)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';

        foreach (var c in attributes)
        {
            if (quote != '\0')
            {
                current.Append(c);
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                current.Append(c);
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private static string AttributeName(string attribute)
    {
        var eq = attribute.IndexOf('=', StringComparison.Ordinal);
        return (eq < 0 ? attribute : attribute[..eq]).Trim();
    }

    private static bool HasAttribute(string attributes, string name)
    {
        foreach (var attribute in SplitAttributes(attributes))
        {
            if (string.Equals(AttributeName(attribute), name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
    ///     Finds <c>&lt;name</c> as a real tag — not as a prefix of a longer one, and not inside a
    ///     comment.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Without the delimiter check, looking for <c>&lt;base</c> also matches a hypothetical
    ///         <c>&lt;basefont&gt;</c>, and looking for <c>&lt;script</c> would match
    ///         <c>&lt;scripts&gt;</c>. A tag name ends at whitespace, <c>/</c>, or <c>&gt;</c>.
    ///     </para>
    ///     <para>
    ///         <b>Comments are skipped, and that was missing.</b> A shell that opens with a comment
    ///         explaining itself — which this repo's own does, and which is the natural thing to write
    ///         at the top of a file the build rewrites — mentions <c>&lt;head&gt;</c> in prose, and the
    ///         search locked onto that. Everything downstream then measured from inside the comment:
    ///         the prefix handed to <see cref="MergeHtmlAttributes" /> contained no <c>&lt;html&gt;</c>
    ///         at all, so it returned unchanged and every attribute a <c>Shell</c> override put on
    ///         <c>&lt;html&gt;</c> was dropped — which is the exact failure that method was written to
    ///         fix. It read as working because the site had already moved <c>data-rask-ui</c> into the
    ///         shell's own literal tag after being bitten by it once, so the one attribute anyone was
    ///         watching survived for a different reason.
    ///     </para>
    /// </remarks>
    private static int IndexOfTag(ReadOnlySpan<char> html, string name)
    {
        var cursor = 0;
        while (cursor < html.Length)
        {
            var rest = html[cursor..];
            var hit = rest.IndexOf($"<{name}", StringComparison.OrdinalIgnoreCase);
            if (hit < 0)
            {
                return -1;
            }

            // A comment that opens before the hit swallows it. Jump past the comment and look again —
            // rather than past the hit, since the tag may legitimately appear after the comment closes.
            var comment = rest.IndexOf("<!--", StringComparison.Ordinal);
            if (comment >= 0 && comment < hit)
            {
                var close = rest[comment..].IndexOf("-->", StringComparison.Ordinal);
                if (close < 0)
                {
                    // Unterminated: everything after it is comment, so there is no tag to find.
                    return -1;
                }

                cursor += comment + close + 3;
                continue;
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
