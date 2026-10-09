using System.Globalization;
using Rask.Core.Diagnostics;

namespace Rask.Core.Live;

/// <summary>
///     Says why a reply went out as the whole page when a diff was expected of it. A whole page is tens of
///     kilobytes where a diff is a few hundred bytes, and until now only the devtools' wire tab showed that one
///     had been sent — nothing said which node the differ gave up on.
/// </summary>
/// <remarks>
///     Called from the branch of <c>LiveSessionBase.TryWriteDiff</c> that falls back, and from
///     <c>WritePayload</c> for a render that never reaches the differ, in Development only: the reply that ships
///     as a diff never reaches here, so the steady state pays nothing for it.
/// </remarks>
internal static class FullPageReply
{
    private const int MarkupShown = 160;

    /// <summary>There was nothing to diff against: the differ holds no earlier render of this session.</summary>
    internal const string NoEarlierRender =
        "There is no earlier render of this session to compare with: this is its first reply, or the first since its render history was dropped.";

    /// <summary>The differ found nothing to patch, and the reply had to go out all the same.</summary>
    internal const string NothingToPatch =
        "The differ found no change in the tree it compares, yet the reply had to go out: it carries a development error to show, or markup changed that the tree does not describe.";

    /// <summary>Reports the reason on <c>Rask.Live</c>. Every whole page that reaches here has one.</summary>
    /// <param name="compared">False when there was no earlier render to diff against.</param>
    /// <param name="ops">The ops the differ produced for this render.</param>
    /// <param name="rawAtRoot">The differ's own verdict that raw markup at the document's level cannot be patched.</param>
    /// <param name="diffBytes">The size of the diff frame when it was built and lost to the page on size, else 0.</param>
    /// <param name="html">The rendered page.</param>
    internal static void Report(bool compared, List<EditOp> ops, bool rawAtRoot, int diffBytes, ReadOnlySpan<char> html)
    {
        // A first reply is expected to be whole. Every other one is a page somebody can fix, so it is a warning:
        // a host that logs warnings and up still shows it.
        if (compared)
        {
            Say(RaskLogLevel.Warning, html.Length, Reason(ops, rawAtRoot, diffBytes, html) ?? NothingToPatch);
        }
        else
        {
            Say(RaskLogLevel.Information, html.Length, NoEarlierRender);
        }
    }

    /// <summary>Reports a whole page sent because the render carries something only a whole page can.</summary>
    /// <param name="signIn">True for a sign-in handoff, false for a file download.</param>
    /// <param name="chars">The length of the rendered page.</param>
    internal static void ReportOutOfBand(bool signIn, int chars) => Say(RaskLogLevel.Information, chars, OutOfBand(signIn));

    /// <summary>Why a render that carries a sign-in handoff or a download is not diffed at all.</summary>
    internal static string OutOfBand(bool signIn) => signIn
        ? "The render carries a sign-in handoff, which travels with a whole page: the connection is replaced under the new principal straight after."
        : "The render carries a file download, which travels with a whole page.";

    private static void Say(RaskLogLevel level, int chars, string reason) =>
        RaskDiagnostics.Report(
            level, "Rask.Live",
            string.Create(CultureInfo.InvariantCulture, $"Rask live: a reply went out as the whole page ({chars} chars) instead of a diff: {reason}"));

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
