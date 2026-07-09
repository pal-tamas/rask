namespace Rask.Core.Live;

/// <summary>
///     Pure diff-codec gating helpers shared by both host sessions
///     (<c>Rask.Server.LiveSession</c> and <c>Rask.Wasm.WasmLiveSession</c>). These carry no
///     transport or session state — they decide, from the rendered HTML and the computed edit
///     ops, whether a diff can be shipped and what slice of <c>&lt;head&gt;</c> rides along.
///     Kept in one place so a fix to the head-fragment gating or the structural-op safety rule
///     applies to both transports at once.
/// </summary>
internal static class LiveDiffGate
{
    // Structural ops (Insert/Remove/Move) route to the full-HTML morph path UNLESS they
    // were produced by FrameDiffer's keyed-matching branch (EditOp.Trusted=true). Keyed
    // matching identifies survivors by data-rask-key, so a structural op only fires when
    // a node truly entered or left the keyed set — focus/listener/IDL state on surviving
    // nodes stays intact (Moves don't materialise new DOM, they re-parent the same node).
    // Positional structural ops can replace mid-list elements that the morph would have
    // preserved, which broke 83/430 e2e tests in an earlier iteration that ungated them
    // unconditionally — that's why we still route those through the full-HTML path.
    internal static bool DiffOpsAreClientSupported(List<EditOp> ops)
    {
        for (var i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            if ((op.Kind == EditOpKind.InsertSubtree
                 || op.Kind == EditOpKind.RemoveSubtree
                 || op.Kind == EditOpKind.MoveSubtree
                 || op.Kind == EditOpKind.PermutationBatch)
                && !op.Trusted)
            {
                return false;
            }
        }

        return true;
    }

    // True when the <head> region (prefix through the closing </head>) of the freshly
    // rendered document is byte-identical to the baseline last sent/applied. The diff codec
    // walks the body but suppresses head-asset frames (HeadAssetRegistry pushes a null
    // FrameSinkScope), so a body-only diff would freeze <head> — wrong <title>, stale
    // scoped-CSS link. Only when the head is unchanged is it safe to ship diff+history on
    // a navigation; otherwise full HTML morphs document.documentElement and picks up the
    // head delta. Both strings are the same root-attr-free HtmlSerializer output
    // (data-rask-root is spliced onto <body> only at payload-write time), so the
    // comparison is stable. A missing </head> in either string returns false (treat as
    // changed → full HTML), never an unsafe true.
    internal static bool HeadUnchanged(ReadOnlySpan<char> html, ReadOnlySpan<char> baseline)
    {
        const string headClose = "</head>";
        var a = html.IndexOf(headClose, StringComparison.Ordinal);
        if (a < 0)
        {
            return false;
        }

        var b = baseline.IndexOf(headClose, StringComparison.Ordinal);
        if (b < 0)
        {
            return false;
        }

        a += headClose.Length;
        b += headClose.Length;
        if (a != b)
        {
            return false;
        }

        return html.Slice(0, a).SequenceEqual(baseline.Slice(0, b));
    }

    // Slice the full <head ...>...</head> element out of the rendered document so the diff
    // payload can carry it for the client to morph into document.head. <head> is a shell tag
    // (no scope attr) and data-rask-root is spliced onto <body> only, so the slice is clean.
    // Returns null when there's no recognizable head (defensive — caller then omits the
    // field and the body diff still ships).
    internal static string? ExtractHead(ReadOnlySpan<char> html)
    {
        var open = html.IndexOf("<head", StringComparison.Ordinal);
        if (open < 0)
        {
            return null;
        }

        const string headClose = "</head>";
        var close = html.IndexOf(headClose, StringComparison.Ordinal);
        if (close < 0)
        {
            return null;
        }

        close += headClose.Length;
        return close <= open ? null : html.Slice(open, close - open).ToString();
    }
}
