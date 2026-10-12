using System.Buffers;
using System.Text;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

/// <summary>
///     What a live session answers one change with, decided the way <c>LiveSessionBase.TryWriteDiff</c> decides
///     it: the ops the differ wrote, whether the gate lets them through, and whether the frame they make is
///     smaller than the page.
/// </summary>
internal sealed class Reply
{
    private Reply(RenderFrame[] oldFrames, RenderFrame[] newFrames, string html)
    {
        NewFrames = newFrames;
        Html = html;
        Before = FrameDom.Of(oldFrames);
        Expected = FrameDom.Of(newFrames).Html;

        var scratch = new FrameDiffer.DiffScratch();
        FrameDiffer.Diff(oldFrames, newFrames, Ops, scratch, out _, html);
        var writer = new ArrayBufferWriter<byte>();
        LivePayload.BuildPayloadUtf8WithRoot(writer, html, "session", null, false);
        PageBytes = writer.WrittenCount;
        writer.ResetWrittenCount();
        LivePayload.BuildPayloadUtf8Diff(writer, Ops, newHtml: html);
        var shipped = LiveDiffGate.DiffOpsAreClientSupported(Ops) && !scratch.ForceFullHtml && writer.WrittenCount < html.Length;
        Bytes = shipped ? writer.WrittenCount : PageBytes;
        Kind = !shipped ? ReplyKind.Page : Ops.Exists(static op => op.Kind == EditOpKind.MorphSubtree) ? ReplyKind.Morph : ReplyKind.Diff;
    }

    /// <summary>The new render's markup, which an insert or a morph cuts its fragment from.</summary>
    internal string Html { get; }

    /// <summary>The frames of the new render.</summary>
    internal RenderFrame[] NewFrames { get; }

    internal List<EditOp> Ops { get; } = [];

    internal ReplyKind Kind { get; }

    /// <summary>The size of the frame that goes out, whichever it is.</summary>
    internal int Bytes { get; }

    /// <summary>The size of the whole page's frame.</summary>
    internal int PageBytes { get; }

    /// <summary>The first render as a document the ops can be applied to.</summary>
    internal FrameDom Before { get; }

    /// <summary>The document the new render describes.</summary>
    internal string Expected { get; }

    /// <summary>The markup each morph carries: the inner HTML of the parent it names.</summary>
    internal IEnumerable<string> Morphed =>
        Ops.Where(static op => op.Kind == EditOpKind.MorphSubtree).Select(op => op.Value ?? Html[op.HtmlStart..op.HtmlEnd]);

    internal static Reply To(Component before, Component after)
    {
        var (afterFrames, html) = Frames(after);
        return new Reply(Frames(before).Frames, afterFrames, html);
    }

    /// <summary>Two renders of one live root, so handler ids are the ones a session would write.</summary>
    internal static Reply ToLive(Component root, Action change)
    {
        var (oldFrames, _) = Live(root);
        change();
        var (newFrames, html) = Live(root);
        return new Reply(oldFrames, newFrames, html);
    }

    internal void Apply() => Before.Apply(Ops, NewFrames);

    private static (RenderFrame[] Frames, string Html) Frames(Component tree)
    {
        var html = new StringBuilder();
        var frames = new FrameWriter();
        using (FrameSinkScope.Push(frames))
        {
            HtmlSerializer.Serialize(tree, html);
        }

        return (frames.WrittenSpan.ToArray(), html.ToString());
    }

    private static (RenderFrame[] Frames, string Html) Live(Component root)
    {
        var frames = new FrameWriter();
        string html;
        using (FrameSinkScope.Push(frames))
        {
            html = root.RenderAsLiveRoot();
        }

        return (frames.WrittenSpan.ToArray(), html);
    }
}

internal enum ReplyKind
{
    /// <summary>Ops that name the nodes that changed.</summary>
    Diff,

    /// <summary>A diff in which at least one parent's children travel as markup.</summary>
    Morph,

    /// <summary>The whole page.</summary>
    Page,
}
