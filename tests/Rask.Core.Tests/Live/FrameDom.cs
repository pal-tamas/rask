using System.Text;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

/// <summary>
///     A document built from a frame stream and patched by edit ops the way the browser's <c>applyDiff</c>
///     patches the page: each op in order, a structural one by its slot among the parent's child nodes.
/// </summary>
/// <remarks>
///     What a test gets from it is the two things a list of ops cannot show: that the ops, applied in the order
///     they were written, leave the document the new render describes — and WHICH node ended up where, since a
///     node that stayed is the same object before and after.
/// </remarks>
internal sealed class FrameDom
{
    private readonly Node _document = new(null, null);

    private FrameDom()
    {
    }

    internal string Html => _document.InnerHtml();

    internal static FrameDom Of(ReadOnlySpan<RenderFrame> frames)
    {
        var dom = new FrameDom();
        Fill(dom._document, frames, 0, frames.Length);
        return dom;
    }

    internal Node At(params int[] path)
    {
        var node = _document;
        foreach (var slot in path)
        {
            node = node.Children[slot];
        }

        return node;
    }

    internal void Apply(IEnumerable<EditOp> ops, ReadOnlySpan<RenderFrame> newFrames)
    {
        foreach (var op in ops)
        {
            switch (op.Kind)
            {
                case EditOpKind.SetAttribute:
                    At(op.Path).Attributes[op.Name!] = op.Value;
                    break;
                case EditOpKind.RemoveAttribute:
                    At(op.Path).Attributes.Remove(op.Name!);
                    break;
                case EditOpKind.UpdateText:
                    At(op.Path).Text = op.Value;
                    break;
                case EditOpKind.InsertSubtree:
                    At(op.Path[..^1]).Children.Insert(op.Path[^1], Inserted(op, newFrames));
                    break;
                case EditOpKind.RemoveSubtree:
                    At(op.Path[..^1]).Children.RemoveRange(op.Path[^1], Math.Max(op.Length, 1));
                    break;
                default:
                    throw new NotSupportedException(op.Kind + " is not an op this document replays.");
            }
        }
    }

    // The op names its fragment by the char range of the new HTML, which is the range one new frame spans.
    private static Node Inserted(EditOp op, ReadOnlySpan<RenderFrame> newFrames)
    {
        for (var i = 0; i < newFrames.Length; i++)
        {
            ref readonly var frame = ref newFrames[i];
            if (frame.Kind != RenderFrameKind.Attribute && frame.HtmlStart == op.HtmlStart && frame.HtmlEnd == op.HtmlEnd)
            {
                var holder = new Node(null, null);
                Fill(holder, newFrames, i, i + frame.SubtreeLength);
                return holder.Children[0];
            }
        }

        throw new InvalidOperationException("An insert carries no fragment the new render wrote.");
    }

    private static void Fill(Node parent, ReadOnlySpan<RenderFrame> frames, int start, int end)
    {
        var i = start;
        while (i < end)
        {
            ref readonly var frame = ref frames[i];
            switch (frame.Kind)
            {
                case RenderFrameKind.Attribute:
                    parent.Attributes[frame.Name!] = frame.Value;
                    break;
                case RenderFrameKind.Element:
                    var element = new Node(frame.Name, null);
                    Fill(element, frames, i + 1, i + frame.SubtreeLength);
                    parent.Children.Add(element);
                    break;
                default:
                    parent.Children.Add(new Node(null, frame.Name));
                    break;
            }

            i += frame.SubtreeLength;
        }
    }

    internal sealed class Node(string? tag, string? text)
    {
        internal string? Tag { get; } = tag;

        internal string? Text { get; set; } = text;

        internal SortedDictionary<string, string?> Attributes { get; } = new(StringComparer.Ordinal);

        internal List<Node> Children { get; } = [];

        internal string InnerHtml()
        {
            var html = new StringBuilder();
            foreach (var child in Children)
            {
                child.Write(html);
            }

            return html.ToString();
        }

        private void Write(StringBuilder html)
        {
            if (Tag is null)
            {
                html.Append('[').Append(Text).Append(']');
                return;
            }

            html.Append('<').Append(Tag);
            foreach (var (name, value) in Attributes)
            {
                html.Append(' ').Append(name).Append("=\"").Append(value).Append('"');
            }

            html.Append('>').Append(InnerHtml()).Append("</").Append(Tag).Append('>');
        }
    }
}
