using Rask.Core;
using Rask.DevTools.Probe;
using Rask.Ui;

namespace Rask.DevTools.Panel;

/// <summary>
///     The Tree tab: the components the inspected page rendered, as they stood after its last render.
/// </summary>
/// <remarks>
///     <para>
///         A snapshot, taken by the probe at the end of the page's render walk and only while this tab is open — the tab
///         says so by holding a watch on the feed for as long as it is mounted. A page nobody is inspecting walks its
///         tree exactly as before.
///     </para>
///     <para>
///         Drawn with the kit's <see cref="UiTree{T,TKey}" />, which is what a component id buys: expansion is keyed on
///         it, so the branches a developer opened survive the next render of the page they are watching.
///     </para>
/// </remarks>
internal sealed partial class DevToolsTreeTab : Component
{
    // One row's height, so a page with thousands of components renders only the rows on screen.
    private const int RowHeight = 24;

    private DevToolsFeed? _following;
    private DevToolsRefreshGate? _gate;
    private IDisposable? _watch;

    /// <summary>The inspected session's feed.</summary>
    public required DevToolsFeed Feed { get; set; }

    /// <inheritdoc />
    protected override void OnMount()
    {
        _gate = new DevToolsRefreshGate(StateHasChanged, CancellationToken);
        _following = Feed;
        _watch = Feed.WatchTree();
        _following.Changed += OnFeedChanged;
    }

    /// <inheritdoc />
    protected override void OnUnmount()
    {
        if (_following is { } feed)
        {
            feed.Changed -= OnFeedChanged;
            _following = null;
        }

        // Giving the watch back is what stops the page paying for a snapshot nobody reads.
        _watch?.Dispose();
        _watch = null;
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (Feed.TreeSnapshot() is not { } root)
        {
            return UiAlert["No tree yet. It arrives with the page's next render."];
        }

        return Div.Class("flex flex-col gap-3")[
            P.Class("text-xs opacity-60")[$"{Count(root)} components, as of the page's last render."],
            UiTree.Roots([root])
                .NodeKey(n => n.Id)
                .Item(Row)
                .Label("Component tree")
                .ExpandDepth(2)
                .NodeText(n => n.Type)
                .Selection(UiTreeSelection.Single)
                .ItemSize(RowHeight)
                .Height(320)[n => n.Children]
        ];
    }

    private static Component Row(DevToolsComponentNode node) =>
        Span.Class("flex items-center gap-2 truncate")[
            Span.Class("truncate")[node.Type],
            node.Key is { Length: > 0 } key
                ? UiBadge.Size(UiSize.Xs).Variant(UiVariant.Soft)[key]
                : Span
        ];

    private static int Count(DevToolsComponentNode node)
    {
        var total = 1;
        foreach (var child in node.Children)
        {
            total += Count(child);
        }

        return total;
    }

    private void OnFeedChanged() => _gate?.Notify();
}
