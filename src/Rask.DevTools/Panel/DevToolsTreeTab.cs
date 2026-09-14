using Rask.Core;
using Rask.Core.Diagnostics.DevTools;
using Rask.DevTools.Probe;
using Rask.Ui;

namespace Rask.DevTools.Panel;

/// <summary>
///     The Tree tab: the components the inspected page rendered, nested as they sit on the page, as they stood after its
///     last render — with the HTML elements between them one toggle away.
/// </summary>
/// <remarks>
///     <para>
///         A snapshot, built while a panel is open: the panel page holds a watch on the feed for as long as it is mounted
///         (see <see cref="DevToolsTreeWatcher" />), so the tree is ready by the time this tab is picked. The tab holds
///         one too, for as long as it is on screen.
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
    private bool _showTags;
    private DevToolsComponentNode? _viewOf;
    private DevToolsComponentNode? _view;

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

    // The toggle is a field, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (Feed.TreeSnapshot() is not { } snapshot)
        {
            return UiAlert["No tree yet. It arrives with the page's first render."];
        }

        var root = View(snapshot);
        return Div.Class("flex flex-col gap-3")[
            Div.Class("flex flex-wrap items-center justify-between gap-2")[
                P.Class("text-xs opacity-60")[$"{Count(root, tags: false)} components, as of the page's last render."],
                UiToggle.Value(_showTags).Text("Show HTML tags").Size(UiSize.Sm).OnChange(v => _showTags = v)
            ],
            UiTree.Roots([root])
                .NodeKey(n => n.Id)
                // A tree per view. ExpandDepth applies to a tree's first render only, so without the key the elements the
                // toggle brings in would arrive collapsed and hide the very components that were open a moment before.
                .Key(_showTags ? "tags" : "components")
                .Item(Row)
                .Label("Component tree")
                // Deep enough to reach a page's own components through the layout and kit components around them — and,
                // with the elements in between, through the <html>, <body> and wrappers around those too.
                .ExpandDepth(_showTags ? 16 : 8)
                .NodeText(n => n.Type)
                .Selection(UiTreeSelection.Single)
                .ItemSize(RowHeight)
                .Height(320)[n => n.Children]
        ];
    }

    // The snapshot always holds the elements; without the toggle a component's elements give way to what is inside them,
    // so a card's rows sit directly under the card. Worked out once per snapshot rather than on every render.
    private DevToolsComponentNode View(DevToolsComponentNode snapshot)
    {
        if (_showTags)
        {
            return snapshot;
        }

        if (!ReferenceEquals(_viewOf, snapshot))
        {
            _viewOf = snapshot;
            _view = snapshot with { Children = WithoutTags(snapshot.Children) };
        }

        return _view!;
    }

    internal static List<DevToolsComponentNode> WithoutTags(IReadOnlyList<DevToolsComponentNode> nodes)
    {
        var kept = new List<DevToolsComponentNode>();
        foreach (var node in nodes)
        {
            if (node.IsTag)
            {
                kept.AddRange(WithoutTags(node.Children));
            }
            else
            {
                kept.Add(node with { Children = WithoutTags(node.Children) });
            }
        }

        return kept;
    }

    private static Component Row(DevToolsComponentNode node) =>
        node.IsTag
            ? Span.Class("truncate font-mono text-xs opacity-60")["<" + node.Type + ">"]
            : ComponentRow(node);

    private static Component ComponentRow(DevToolsComponentNode node) =>
        Span.Class("flex items-center gap-2 truncate")[
            Span.Class("truncate")[node.Type],
            node.Key is { Length: > 0 } key
                ? UiBadge.Size(UiSize.Xs).Variant(UiVariant.Soft)[key]
                : Span,
            // What the component was given, on the row itself: a tree whose rows say only their type names makes a
            // developer click every one of them to find the value they came for.
            node.Props.Count == 0
                ? Span
                : Span.Class("truncate text-xs opacity-60")[string.Join("  ", node.Props.Select(Described))]
        ];

    private static string Described(DescribedProp prop) =>
        prop.Name + "=" + (prop.Value is null ? "null" : prop.Value);

    private static int Count(DevToolsComponentNode node, bool tags)
    {
        var total = node.IsTag && !tags ? 0 : 1;
        foreach (var child in node.Children)
        {
            total += Count(child, tags);
        }

        return total;
    }

    private void OnFeedChanged() => _gate?.Notify();
}
