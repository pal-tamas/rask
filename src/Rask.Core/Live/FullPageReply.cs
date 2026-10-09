using System.Globalization;
using Rask.Core.Diagnostics;

namespace Rask.Core.Live;

/// <summary>
///     Says why a reply went out as the whole page when a diff had been computed for it. A whole page is tens of
///     kilobytes where a diff is a few hundred bytes, and until now only the devtools' wire tab showed that one
///     had been sent — nothing said which node the differ gave up on.
/// </summary>
/// <remarks>
///     Called from the one branch of <c>LiveSessionBase.TryWriteDiff</c> that falls back, in Development only:
///     the reply that ships as a diff never reaches here, so the steady state pays nothing for it.
/// </remarks>
internal static class FullPageReply
{
    private const int MarkupShown = 160;

    /// <summary>Reports the reason on <c>Rask.Live</c>, when there is one to give.</summary>
    internal static void Report(List<EditOp> ops, bool rawAtRoot, int diffBytes, ReadOnlySpan<char> html)
    {
        if (Reason(ops, rawAtRoot, diffBytes, html) is { } reason)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Information, "Rask.Live",
                string.Create(CultureInfo.InvariantCulture, $"Rask live: a reply went out as the whole page ({html.Length} chars) instead of a diff: {reason}"));
        }
    }

    /// <summary>
    ///     Why the diff in <paramref name="ops" /> was not sent, or <c>null</c> when it was not refused at all —
    ///     the render changed nothing, so there was no diff to send.
    /// </summary>
    /// <param name="ops">The ops the differ produced for this render.</param>
    /// <param name="rawAtRoot">The differ's own verdict that raw markup at the document's level cannot be patched.</param>
    /// <param name="diffBytes">The size of the diff frame when it was built and lost to the page on size, else 0.</param>
    /// <param name="html">The rendered page, which an insert's markup is cut from.</param>
    internal static string? Reason(List<EditOp> ops, bool rawAtRoot, int diffBytes, ReadOnlySpan<char> html)
    {
        if (diffBytes > 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"The diff was {diffBytes} bytes, no smaller than the page itself.");
        }

        if (rawAtRoot)
        {
            return "Raw markup sits beside other nodes at the document's own level, where there is no element whose children could be replaced instead.";
        }

        var refused = LiveDiffGate.FirstRefused(ops);
        if (refused < 0)
        {
            return null;
        }

        var op = ops[refused];
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{op.Kind} at /{string.Join('/', op.Path)} is by position{Arriving(ops, refused, html)}. A child that comes and goes before its siblings, or changes element, is patched in place only when it and its siblings each carry a Key; without one the differ cannot tell which node stayed.");
    }

    // The markup that arrives at the refused slot, which names the element far better than a path does: the
    // op's own when it is an insert, else the insert that replaces what the op removed.
    private static string Arriving(List<EditOp> ops, int refused, ReadOnlySpan<char> html)
    {
        for (var i = refused; i < ops.Count && i <= refused + 1; i++)
        {
            var op = ops[i];
            if (op.Kind == EditOpKind.InsertSubtree && op.HtmlStart >= 0 && op.HtmlEnd <= html.Length
                && op.Path.AsSpan().SequenceEqual(ops[refused].Path))
            {
                var markup = html[op.HtmlStart..op.HtmlEnd];
                var cut = markup.Length > MarkupShown ? string.Concat(markup[..MarkupShown], "…") : markup.ToString();
                return i == refused ? ", inserting " + cut : ", replaced by " + cut;
            }
        }

        return string.Empty;
    }
}
