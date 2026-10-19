using System.Globalization;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Diagnostics.DevTools;
using Rask.Core.Live;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Panel;

/// <summary>
///     The component tree the Tree tab shows: rows a developer opens, walks with the keyboard and selects one of.
/// </summary>
/// <remarks>
///     <para>
///         Flux has no tree, so this is the panel's own, drawn with Flux's icons and Flux's navigation-list colours. It
///         is the WAI-ARIA tree: one focusable element, a cursor inside it named by <c>aria-activedescendant</c> (which the
///         runtime scrolls into view, and whose arrow keys it keeps from scrolling the panel), and type-ahead on the
///         component names.
///     </para>
///     <para>
///         Virtualized: a page with thousands of components renders only the rows on screen, each exactly
///         <see cref="RowHeight" /> tall, so the levels are carried by <c>aria-level</c> and an indent rather than by
///         nesting. Expansion and selection are the tab's, which a pick or the Errors tab's "Show in tree" changes too.
///     </para>
/// </remarks>
internal sealed partial class DevToolsTree : Component
{
    private const string Prefix = "rask-devtools-tree";
    private const int RowHeight = 24;
    private const int ViewHeight = 320;
    private const int PageRows = ViewHeight / RowHeight;

    private readonly Dictionary<long, int> _index = [];
#pragma warning disable S3459 // a mutable struct whose default is its empty state; Next() fills it in place
    private DevToolsTypeAhead _typeAhead;
#pragma warning restore S3459
    private long? _cursor;
    private int _cursorHint;

    // The selection as the last render saw it, and the last one this tree reported: together they say whether a change
    // came from the tab (a pick — the cursor goes to it) or is the tab handing back what the developer just clicked.
    private long? _selectionSeen;
    private bool _seenOnce;
    private long? _selectionReported;

    /// <summary>The root of the view on screen.</summary>
    public required DevToolsComponentNode Root { get; set; }

    /// <summary>The ids whose children are shown.</summary>
    public required IReadOnlyCollection<long> Expanded { get; set; }

    /// <summary>The selected node.</summary>
    public long? Selected { get; set; }

    /// <summary>Raised with the whole expanded set after every expand or collapse.</summary>
    public Callback<IReadOnlyList<long>> OnExpand { get; set; }

    /// <summary>Raised with the node a click, Enter or Space selected.</summary>
    public Callback<long> OnSelect { get; set; }

    // The cursor and the row index are fields, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        _index.Clear();
        var rows = DevToolsTreeRow.Visible(Root, Expanded, _index);
        FollowSelection();
        var cursor = Cursor(rows);

        return Virtualize.Items<DevToolsTreeRow>(
            ctx => Ul
                .Id(Prefix)
                .Role("tree")
                .TabIndex(0)
                .Class("group w-full overflow-y-auto outline-none")
                .Style("height:" + Px(ViewHeight))
                // What the runtime's scroll-follow needs to reach a row that is not rendered: where it would be.
                .Data(("rask-item-size", Int(RowHeight)), ("rask-active-top", Int(Math.Max(cursor, 0) * RowHeight)))
                .Aria(TreeAria(rows, cursor))
                .OnScroll(ctx.OnScroll)
                .OnKeyDown(e => OnKey(e, rows))[
                    Spacer("before", ctx.OffsetBefore),
                    ctx.VisibleItems.Select(v => Row(v.Value, v.Index == cursor)),
                    Spacer("after", ctx.OffsetAfter)
                ],
            rows,
            ItemSize: RowHeight,
            OverscanCount: 8,
            InitialClientHeight: ViewHeight);
    }

    private Component Row(DevToolsTreeRow row, bool isCursor)
    {
        var id = row.Node.Id;
        var selected = id == Selected;
        return Li
            .Key(id)
            .Id(RowId(id))
            .Role("treeitem")
            .Class("overflow-hidden")
            .Style("height:" + Px(RowHeight))
            .Data("rask-key", RowId(id))
            .Aria(RowAria(row, selected))[
                Div
                    .Id(RowId(id) + "-l")
                    .Class(RowClass(selected, isCursor))
                    // The indent is a style rather than a class: a class built from the depth is one Tailwind never sees.
                    .Style("padding-inline-start:calc(0.25rem + " + Int(row.Level - 1) + " * 1rem)")
                    .OnClick(() => Click(row))[
                        Twisty(row),
                        Item(row.Node)
                    ]
            ];
    }

    private static Dictionary<string, string?> TreeAria(List<DevToolsTreeRow> rows, int cursor)
    {
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal) { ["label"] = "Component tree" };
        if (cursor >= 0)
        {
            aria["activedescendant"] = RowId(rows[cursor].Node.Id);
        }

        return aria;
    }

    private static Dictionary<string, string?> RowAria(DevToolsTreeRow row, bool selected)
    {
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["labelledby"] = RowId(row.Node.Id) + "-l",
            ["selected"] = Bool(selected),
            ["level"] = Int(row.Level),
            ["setsize"] = Int(row.SetSize),
            ["posinset"] = Int(row.PosInSet),
        };
        if (row.HasChildren)
        {
            aria["expanded"] = Bool(row.IsExpanded);
        }

        return aria;
    }

    // Flux's navigation-list colours: the selected row lifted, the others quiet until the pointer is on them. The cursor
    // is ringed only while the tree has the keyboard.
    private static string RowClass(bool selected, bool isCursor) =>
        "rask-devtools-row flex h-full min-w-0 items-center gap-1 rounded-md pe-2 text-sm "
        + (selected
            ? "bg-zinc-800/5 text-zinc-800 dark:bg-white/7 dark:text-white"
            : "text-zinc-500 hover:bg-zinc-800/[4%] hover:text-zinc-800 dark:text-white/80 dark:hover:bg-white/[7%] dark:hover:text-white")
        + (isCursor ? " group-focus:outline-2 group-focus:-outline-offset-2 group-focus:outline-zinc-800 dark:group-focus:outline-white" : "");

    // A leaf keeps the same box, so its name lines up with its siblings' names rather than with their chevrons.
    private Component Twisty(DevToolsTreeRow row)
    {
        var box = Span.Class("inline-flex w-4 shrink-0 justify-center").Aria("hidden", "true");
        if (!row.HasChildren)
        {
            return box;
        }

        var chevron = row.IsExpanded ? Ui.IconName.ChevronDown : Ui.IconName.ChevronRight;
        return box.OnClick(() => SetExpanded([row.Node.Id], !row.IsExpanded))[Ui.Icon.Name(chevron).Micro.Class("size-3")];
    }

    private static Component Item(DevToolsComponentNode node)
    {
        if (node.IsTag)
        {
            return Span.Class("truncate font-mono text-xs opacity-60").Data(Place(node))[Label(node)];
        }

        // An empty span holds each optional part's place, so a row keeps its shape as a key or a badge comes and goes.
        return Span.Class("flex items-center gap-2 truncate").Data(Place(node))[
            Span.Class("truncate")[node.Type],
            node.Badge is { } badge ? Ui.Badge.Sm.Color(Ui.Color.Blue)[badge] : Span,
            node.Key is { Length: > 0 } key ? Ui.Badge.Sm[key] : Span,
            // What the component was given, on the row itself: a tree whose rows say only their type names makes a
            // developer click every one of them to find the value they came for.
            node.Props.Count == 0 ? Span : Span.Class("truncate text-xs opacity-60")[string.Join("  ", node.Props.Select(Described))]
        ];
    }

    private static string Described(DescribedProp prop) => prop.Name + "=" + (prop.Value ?? "null");

    // Qualified: inside markup the bare type name is its chain entry.
    private static string Label(DevToolsComponentNode node) => global::Rask.DevTools.Panel.DevToolsTreeTab.Label(node);

    private static readonly IReadOnlyDictionary<string, string?> Nowhere = new Dictionary<string, string?>(StringComparer.Ordinal);

    // Where the node is, for the panel's script to hand to the page when the pointer is on this row.
    private static IReadOnlyDictionary<string, string?> Place(DevToolsComponentNode node) =>
        node.At is null
            ? Nowhere
            : new AttrBag([("rask-devtools-at", node.At), ("rask-devtools-label", Label(node))]);

    private static HTMLLIElement Spacer(string which, int height) =>
        Li.Key("rask-devtools-tree-" + which).Role("none").Style("height:" + Px(height));

    // ---- the keyboard ---------------------------------------------------------------------------

    private async Task OnKey(KeyboardEvent e, List<DevToolsTreeRow> rows)
    {
        // A modified key belongs to the browser or the app, not to the tree.
        if (rows.Count == 0 || e.CtrlKey || e.AltKey || e.MetaKey)
        {
            return;
        }

        var at = Math.Clamp(Cursor(rows), 0, rows.Count - 1);
        if (Destination(e.Key, at, rows.Count) is { } to)
        {
            MoveTo(rows, to);
            return;
        }

        var row = rows[at];
        switch (e.Key)
        {
            case Keys.ArrowRight when row.HasChildren && !row.IsExpanded:
                await SetExpanded([row.Node.Id], true).ConfigureAwait(false);
                break;
            case Keys.ArrowRight when row.IsExpanded:
                MoveTo(rows, at + 1);
                break;
            case Keys.ArrowLeft when row.IsExpanded:
                await SetExpanded([row.Node.Id], false).ConfigureAwait(false);
                break;
            case Keys.ArrowLeft when row.Parent >= 0:
                MoveTo(rows, row.Parent);
                break;
            case "*":
                await SetExpanded([.. rows.Where(r => r.Parent == row.Parent && r.HasChildren).Select(r => r.Node.Id)], true)
                    .ConfigureAwait(false);
                break;
            case Keys.Enter or " ":
                await Select(row.Node.Id).ConfigureAwait(false);
                break;
            default:
                TypeAhead(e.Key, at, rows);
                break;
        }
    }

    // Where a key that only moves the cursor moves it to, or null for a key that does something else.
    private static int? Destination(string key, int at, int count) => key switch
    {
        Keys.ArrowDown => Math.Min(at + 1, count - 1),
        Keys.ArrowUp => Math.Max(at - 1, 0),
        Keys.Home => 0,
        Keys.End => count - 1,
        Keys.PageDown => Math.Min(at + PageRows, count - 1),
        Keys.PageUp => Math.Max(at - PageRows, 0),
        _ => null,
    };

    private void TypeAhead(string key, int at, List<DevToolsTreeRow> rows)
    {
        if (key.Length != 1)
        {
            return;
        }

        var hit = _typeAhead.Next(key, at, [.. rows.Select(r => r.Node.Type)], TimeProvider.System);
        if (hit >= 0)
        {
            MoveTo(rows, hit);
        }
    }

    private Task Click(DevToolsTreeRow row)
    {
        _cursor = row.Node.Id;
        _cursorHint = _index.TryGetValue(row.Node.Id, out var at) ? at : 0;
        return Select(row.Node.Id);
    }

    private Task Select(long id)
    {
        _selectionReported = id;
        return OnSelect.Invoke(id).AsTask();
    }

    private Task SetExpanded(IReadOnlyList<long> ids, bool open)
    {
        var next = new HashSet<long>(Expanded);
        foreach (var id in ids)
        {
            if (open)
            {
                next.Add(id);
            }
            else
            {
                next.Remove(id);
            }
        }

        return OnExpand.Invoke([.. next]).AsTask();
    }

    // ---- the cursor -----------------------------------------------------------------------------

    // A selection the TAB made — a pick, "Show in tree" — takes the cursor with it, which is what scrolls the node into
    // view. A selection the developer made, handed straight back, leaves the cursor where they put it.
    private void FollowSelection()
    {
        var previous = _selectionSeen;
        var first = !_seenOnce;
        _selectionSeen = Selected;
        _seenOnce = true;
        var reported = _selectionReported;
        _selectionReported = null;

        // On the first render the cursor already starts on the selected row.
        if (first || Selected == previous || Selected == reported)
        {
            return;
        }

        if (Selected is { } selected && _index.TryGetValue(selected, out var at))
        {
            _cursor = selected;
            _cursorHint = at;
        }
    }

    // By id, so it survives everything above it opening or closing; by its last position when the node is gone, which is
    // what a developer means by "where I was" after the node they were on disappeared.
    private int Cursor(List<DevToolsTreeRow> rows)
    {
        if (rows.Count == 0)
        {
            return -1;
        }

        if (_cursor is { } id && _index.TryGetValue(id, out var at))
        {
            _cursorHint = at;
            return at;
        }

        if (_cursor is not null)
        {
            var clamped = Math.Clamp(_cursorHint, 0, rows.Count - 1);
            _cursor = rows[clamped].Node.Id;
            return clamped;
        }

        return Selected is { } selected && _index.TryGetValue(selected, out var selectedAt) ? selectedAt : 0;
    }

    private void MoveTo(List<DevToolsTreeRow> rows, int at)
    {
        at = Math.Clamp(at, 0, rows.Count - 1);
        _cursor = rows[at].Node.Id;
        _cursorHint = at;
    }

    private static string RowId(long id) => Prefix + "-n-" + id.ToString(CultureInfo.InvariantCulture);

    private static string Px(int value) => Int(value) + "px";

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Bool(bool value) => value ? "true" : "false";
}
