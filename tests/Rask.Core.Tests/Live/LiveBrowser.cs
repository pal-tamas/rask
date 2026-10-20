using System.Text.Json;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

/// <summary>
///     A live root and the page a browser holds of it: every render after the first is diffed, the ops are replayed
///     onto that page, and a click is read from it — the handler id and the number the page was sent under.
/// </summary>
internal sealed class LiveBrowser
{
    private readonly Component _root;
    private readonly FrameDiffer.DiffScratch _scratch = new();
    private RenderFrame[] _frames;
    private int _version;

    private LiveBrowser(Component root)
    {
        _root = root;
        (_frames, _) = Render(root);
        Page = FrameDom.Of(_frames);
        root.HandlersDelivered();
    }

    internal FrameDom Page { get; }

    internal static LiveBrowser Open(Component root) => new(root);

    /// <summary>The rows of the page's first table body, in the order the browser shows them.</summary>
    internal List<FrameDom.Node> Rows() => Find(Page.At(), static node => node.Tag == "tbody")!.Children;

    internal Task Click(FrameDom.Node button) => Fire(button, "click", string.Empty);

    /// <summary>A field's <c>change</c>, carrying the value the browser reports: a checkbox's is "true" or "false".</summary>
    internal Task Change(FrameDom.Node field, string value) => Fire(field, "change", ",\"value\":\"" + value + "\"");

    private async Task Fire(FrameDom.Node target, string type, string fields)
    {
        var id = target.Attributes["data-rask-on-" + type]!;
        var payload = JsonDocument.Parse($$"""{"id":"{{id}}","type":"{{type}}","v":{{_version}}{{fields}}}""").RootElement;

        Assert.True(await _root.TryInvokeHandlerAsync(id, payload), "the " + type + " ran nothing");
        Rerender();
    }

    internal void Rerender()
    {
        var (frames, html) = Render(_root);
        var ops = new List<EditOp>();
        FrameDiffer.Diff(_frames, frames, ops, _scratch, out _, html);
        if (_root.HandlerVersionToSend(whole: false) is { } version)
        {
            _version = version;
        }

        Page.Apply(ops, frames);
        _root.HandlersSent();
        _frames = frames;
        Assert.Equal(FrameDom.Of(frames).Html, Page.Html);
    }

    internal static FrameDom.Node? Find(FrameDom.Node node, Func<FrameDom.Node, bool> match)
    {
        if (match(node))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            if (Find(child, match) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static (RenderFrame[] Frames, string Html) Render(Component root)
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
