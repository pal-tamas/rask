using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Rask;
using Rask.Core;
using Rask.Core.Diagnostics.DevTools;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Panel;

/// <summary>
///     The Tree tab: the components the inspected page rendered, nested as they sit on the page, as they stood after its
///     last render — with the HTML elements between them one toggle away, a box on the page around whatever row the
///     pointer is on, and a picker that goes the other way.
/// </summary>
/// <remarks>
///     <para>
///         A snapshot, built while a panel is open: the panel page holds a watch on the feed for as long as it is mounted
///         (see <see cref="DevToolsTreeWatcher" />), so the tree is ready by the time this tab is picked. The tab holds
///         one too, for as long as it is on screen.
///     </para>
///     <para>
///         The page itself is never touched from here. Each row carries where its node is on the page
///         (<c>data-rask-devtools-at</c>), and the panel's own script posts that to the page's devtools host, which draws
///         the box — so a hover costs no round trip to the app. Picking works the same way back: while picking, the tab
///         publishes every node's place (<c>data-rask-devtools-anchors</c>), the host matches what is under the pointer
///         against them, and a click comes back as a keydown on a hidden element — the one event both panel hosts forward
///         with a value.
///     </para>
/// </remarks>
internal sealed partial class DevToolsTreeTab : Component
{
    /// <summary>The <c>key</c> of the keydown a pick arrives as: this, then the picked node's id, or <c>cancel</c>.</summary>
    internal const string PickKeyPrefix = "pick:";

    // One row's height, so a page with thousands of components renders only the rows on screen.
    private const int RowHeight = 24;

    // How deep a view opens the first time it is shown: far enough to reach a page's own components through the layout
    // and kit components around them — and, with the elements in between, through <html>, <body> and their wrappers too.
    private const int ComponentDepth = 8;
    private const int TagDepth = 16;

    private readonly HashSet<long> _expanded = [];
    private DevToolsFeed? _following;
    private DevToolsRefreshGate? _gate;
    private IDisposable? _watch;
    private bool _showTags;
    private bool _seededComponents;
    private bool _seededTags;
    private bool _picking;
    private long? _selected;
    private DevToolsComponentNode? _viewOf;
    private DevToolsComponentNode? _view;

    /// <summary>The inspected session's feed.</summary>
    public required DevToolsFeed Feed { get; set; }

    /// <summary>A component to open the way to and select, once — the Errors tab's "Show in tree".</summary>
    public long? Reveal { get; set; }

    private long? _revealed;

    // The toggle, the picker, expansion and selection are fields, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

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
        if (Feed.TreeSnapshot() is not { } snapshot)
        {
            return Ui.Alert["No tree yet. It arrives with the page's first render."];
        }

        var root = View(snapshot);
        Seed(root);
        if (Reveal is { } reveal && reveal != _revealed)
        {
            _revealed = reveal;
            Open(root, reveal);
        }

        return Div.Class("flex flex-col gap-3")[
            Div.Class("flex flex-wrap items-center justify-between gap-2")[
                P.Class("text-xs opacity-60")[$"{Count(root)} components, as of the page's last render."],
                Div.Class("flex items-center gap-3")[
                    Ui.Button
                        .Size(Ui.Size.Sm)
                        // daisyUI's own marker, written whole: a composed class name is invisible to the kit's Tailwind scan.
                        .Class(_picking ? "btn-active" : null)
                        .Title(_picking ? "Click something on the page, or press Esc" : "Pick something on the page")
                        .Aria(new Dictionary<string, string?> { ["pressed"] = _picking ? "true" : "false" })
                        .OnClick(() => _picking = !_picking)[Ui.Icon.Name(Ui.IconName.Cursor), "Pick"],
                    Ui.Toggle.Value(_showTags).Text("Show HTML tags").Size(Ui.Size.Sm).OnChange(v => _showTags = v)
                ]
            ],
            // Where the panel's script finds what to tell the page, and where it reports a pick back; neither is seen.
            _picking
                ? Div.Key("anchors").Hidden(true)
                    .Data(new Dictionary<string, string?> { ["rask-devtools-anchors"] = Anchors(root) })
                : Span.Key("anchors").Hidden(true),
            Span.Key("picked").Hidden(true)
                .Data(new Dictionary<string, string?> { ["rask-devtools-picked"] = "" })
                .OnKeyDown(e => Picked(e.Key)),
            // The tree and the selected node's details side by side, or one above the other in a narrow drawer.
            Div.Class("flex flex-wrap gap-3")[
                Div.Class("min-w-0").Style("flex:2 1 22rem")[TreeView(root)],
                Div.Class("min-w-0").Style("flex:1 1 16rem")[
                    Details(_selected is { } id ? Find(root, id) : null, provider => Open(root, provider))
                ]
            ]
        ];
    }

    private Component TreeView(DevToolsComponentNode root) =>
            Ui.Tree.Roots([root])
                .NodeKey(n => n.Id)
                .Item(Row)
                .Label("Component tree")
                .NodeText(n => n.Type)
                .Selection(Ui.TreeSelection.Single)
                // Both held here, so a pick can open the picked node's ancestors and select it in one render — which is
                // also what moves the tree's cursor to it, and so scrolls it into view.
                .Expanded([.. _expanded])
                .OnExpandedChange(keys =>
                {
                    _expanded.Clear();
                    _expanded.UnionWith(keys);
                })
                .Selected(_selected is { } selected ? [selected] : [])
                .OnSelectionChange(keys => _selected = keys.Count > 0 ? keys[0] : null)
                .ItemSize(RowHeight)
                .Height(320)[n => n.Children];

    // What the selected node is and what it was given, in full: a row has room for a line of props, this has room for
    // their types and every value — and for the context it provides and reads, each read naming, as a link, the component
    // that provided it.
    internal static Component Details(DevToolsComponentNode? node, Action<long>? open = null)
    {
        if (node is null)
        {
            return P.Class("text-xs opacity-60")["Select a component to see its props."];
        }

        if (node.IsTag)
        {
            return Div.Class("flex flex-col gap-1")[
                Span.Class("font-mono font-semibold")[Label(node)],
                P.Class("text-xs opacity-60")["An HTML element, rendered by the component above it."]
            ];
        }

        return Div.Class("flex flex-col gap-2").Data(new Dictionary<string, string?> { ["rask-devtools-details"] = "" })[
            Div.Class("flex flex-wrap items-center gap-2")[
                Span.Class("font-mono font-semibold")[node.Type],
                node.Badge is { } badge ? Ui.Badge.Size(Ui.Size.Sm).Tone(Ui.Tone.Info).Variant(Ui.Variant.Soft)[badge] : null,
                node.Key is { Length: > 0 } key ? Ui.Badge.Size(Ui.Size.Sm).Variant(Ui.Variant.Soft)["key " + key] : null
            ],
            node.Badge is "Blazor" or null
                ? null
                : P.Class("text-xs opacity-60")[
                    "An island: its own components live in the browser, so the tree ends here. These are the props C# passed it."
                ],
            node.Props.Count == 0
                ? P.Class("text-xs opacity-60")["No props."]
                : Ui.Table.Scroll(true)[
                    Thead[Tr[Th["Prop"], Th["Type"], Th["Value"]]],
                    Tbody[node.Props.Select(PropRow).ToArray()]
                ],
            ContextDetails(node, open)
        ];
    }

    private static Component ContextDetails(DevToolsComponentNode node, Action<long>? open)
    {
        if (node.Provides is null || node.Reads is null)
        {
            return P.Class("text-xs opacity-60").Data(new Dictionary<string, string?> { ["rask-devtools-context"] = "" })[
                "Context shows from the page's next render."
            ];
        }

        if (node.Provides.Count == 0 && node.Reads.Count == 0)
        {
            return P.Class("text-xs opacity-60").Data(new Dictionary<string, string?> { ["rask-devtools-context"] = "" })[
                "Provides and reads no context."
            ];
        }

        return Div.Class("flex flex-col gap-2").Data(new Dictionary<string, string?> { ["rask-devtools-context"] = "" })[
            node.Provides.Count == 0
                ? null
                : Ui.Table.Scroll(true).Data(new Dictionary<string, string?> { ["rask-devtools-provides"] = "" })[
                    Thead[Tr[Th["Provides"], Th["Name"], Th["Value"]]],
                    Tbody[node.Provides.Select(ProvidedRow).ToArray()]
                ],
            node.Reads.Count == 0
                ? null
                : Ui.Table.Scroll(true).Data(new Dictionary<string, string?> { ["rask-devtools-reads"] = "" })[
                    Thead[Tr[Th["Reads"], Th["Name"], Th["From"]]],
                    Tbody[node.Reads.Select(read => ReadRow(read, open)).ToArray()]
                ]
        ];
    }

    private static Component ProvidedRow(DevToolsProvidedContext provided, int index) =>
        Tr.Key(index)[
            Td.Class("font-mono")[provided.Type],
            Td.Class("font-mono text-xs")[provided.Name is null ? Span.Class("opacity-60")["—"] : provided.Name],
            Td.Class("font-mono break-all")[
                provided.IsRedacted
                    ? Span.Title("Not read: its name or type says it is a secret.")[provided.Value ?? PropsDescriber.Redacted]
                    : provided.Value is null ? Span.Class("opacity-60")["null"] : provided.Value
            ]
        ];

    private static Component ReadRow(DevToolsReadContext read, Action<long>? open) =>
        Tr.Key(read.Type + "|" + read.Name)[
            Td.Class("font-mono")[read.Type],
            Td.Class("font-mono text-xs")[read.Name is null ? Span.Class("opacity-60")["—"] : read.Name],
            Td[
                !read.Found
                    ? Span.Class("text-xs opacity-60")["none in scope"]
                    : read.ProviderId is { } provider && open is not null
                        ? Ui.Button.Size(Ui.Size.Xs).Variant(Ui.Variant.Link).Title("Select the component that provided it")
                            .OnClick(() => open(provider))[read.ProviderType ?? "?"]
                        : Span.Class("font-mono")[read.ProviderType ?? "a provider"]
            ]
        ];

    private static Component PropRow(DescribedProp prop) =>
        Tr.Key(prop.Name)[
            Td.Class("font-mono")[prop.Name],
            Td.Class("font-mono text-xs opacity-60").Title(prop.Type)[ShortType(prop.Type)],
            Td.Class("font-mono break-all")[
                prop.IsRedacted
                    ? Span.Title("Not read: the build treats this prop as sensitive.")[prop.Value ?? "••••"]
                    : prop.Value is null ? Span.Class("opacity-60")["null"] : prop.Value
            ]
        ];

    // `System.Collections.Generic.List<Shop.Row>` reads as `List<Row>`: the full name is on the cell's title.
    internal static string ShortType(string type)
    {
        var shortened = new System.Text.StringBuilder(type.Length);
        var segment = 0;
        for (var i = 0; i < type.Length; i++)
        {
            var c = type[i];
            if (c == '.')
            {
                shortened.Length = segment;
                continue;
            }

            shortened.Append(c);
            if (c is '<' or ',' or ' ' or '[' or '(')
            {
                segment = shortened.Length;
            }
        }

        return shortened.ToString();
    }

    private static DevToolsComponentNode? Find(DevToolsComponentNode node, long id)
    {
        if (node.Id == id)
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            if (Find(child, id) is { } found)
            {
                return found;
            }
        }

        return null;
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

    // Each view opens to its depth the first time it is shown; after that, what is open is what the developer opened.
    private void Seed(DevToolsComponentNode root)
    {
        if (_showTags ? _seededTags : _seededComponents)
        {
            return;
        }

        if (_showTags)
        {
            _seededTags = true;
        }
        else
        {
            _seededComponents = true;
        }

        Open(root, _showTags ? TagDepth : ComponentDepth);
    }

    private void Open(DevToolsComponentNode node, int depth)
    {
        if (depth <= 0 || node.Children.Count == 0)
        {
            return;
        }

        _expanded.Add(node.Id);
        foreach (var child in node.Children)
        {
            Open(child, depth - 1);
        }
    }

    // A pick, as the panel's script reports it. Looked up in the view on screen now rather than in a tree a handler closed
    // over, so the result never depends on which render's handler the runtime happens to call: an element's id exists only
    // in the view with tags shown. A node that is gone by now (the page re-rendered between hover and click) ends the pick.
    private void Picked(string key)
    {
        if (!key.StartsWith(PickKeyPrefix, StringComparison.Ordinal))
        {
            return;
        }

        _picking = false;
        if (!long.TryParse(key.AsSpan(PickKeyPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            || Feed.TreeSnapshot() is not { } snapshot)
        {
            return;
        }

        Open(View(snapshot), id);
    }

    // Selects a node and opens the way to it, which also moves the tree's cursor there and scrolls it into view. A node the
    // view does not hold — gone since, or an element while tags are hidden — changes nothing.
    private void Open(DevToolsComponentNode root, long id)
    {
        var ancestors = new List<long>();
        if (PathTo(root, id, ancestors))
        {
            _expanded.UnionWith(ancestors);
            _selected = id;
        }
    }

    internal static bool PathTo(DevToolsComponentNode node, long id, List<long> ancestors)
    {
        if (node.Id == id)
        {
            return true;
        }

        ancestors.Add(node.Id);
        foreach (var child in node.Children)
        {
            if (PathTo(child, id, ancestors))
            {
                return true;
            }
        }

        ancestors.RemoveAt(ancestors.Count - 1);
        return false;
    }

    // [[id,"at","label"],…] for every node of the view that has a place on the page, in tree order — so where two nodes
    // cover the same element, the one further down the tree comes later and is the one a pick lands on.
    internal static string Anchors(DevToolsComponentNode root)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            WriteAnchors(writer, root);
            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteAnchors(Utf8JsonWriter writer, DevToolsComponentNode node)
    {
        if (node.At is { } at)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(node.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteStringValue(at);
            writer.WriteStringValue(Label(node));
            writer.WriteEndArray();
        }

        foreach (var child in node.Children)
        {
            WriteAnchors(writer, child);
        }
    }

    private static string Label(DevToolsComponentNode node) => node.IsTag ? "<" + node.Type + ">" : node.Type;

    // Where the node is, for the panel's script to hand to the page when the pointer is on this row.
    private static Dictionary<string, string?> Place(DevToolsComponentNode node) =>
        node.At is null
            ? []
            : new Dictionary<string, string?> { ["rask-devtools-at"] = node.At, ["rask-devtools-label"] = Label(node) };

    private static Component Row(DevToolsComponentNode node) =>
        node.IsTag
            ? Span.Class("truncate font-mono text-xs opacity-60").Data(Place(node))[Label(node)]
            : ComponentRow(node);

    private static Component ComponentRow(DevToolsComponentNode node) =>
        Span.Class("flex items-center gap-2 truncate").Data(Place(node))[
            Span.Class("truncate")[node.Type],
            node.Badge is { } badge
                ? Ui.Badge.Size(Ui.Size.Xs).Tone(Ui.Tone.Info).Variant(Ui.Variant.Soft)[badge]
                : Span,
            node.Key is { Length: > 0 } key
                ? Ui.Badge.Size(Ui.Size.Xs).Variant(Ui.Variant.Soft)[key]
                : Span,
            // What the component was given, on the row itself: a tree whose rows say only their type names makes a
            // developer click every one of them to find the value they came for.
            node.Props.Count == 0
                ? Span
                : Span.Class("truncate text-xs opacity-60")[string.Join("  ", node.Props.Select(Described))]
        ];

    private static string Described(DescribedProp prop) =>
        prop.Name + "=" + (prop.Value is null ? "null" : prop.Value);

    private static int Count(DevToolsComponentNode node)
    {
        var total = node.IsTag ? 0 : 1;
        foreach (var child in node.Children)
        {
            total += Count(child);
        }

        return total;
    }

    private void OnFeedChanged() => _gate?.Notify();
}
