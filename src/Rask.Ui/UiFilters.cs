using System.Globalization;
using System.Runtime.CompilerServices;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     The filters of a list, kept in the page's address: a "Filters" button that opens them in a panel, and badges
///     that say which are in force.
/// </summary>
/// <remarks>
///     <code>
///     var filters = Ui.Filters[
///         Ui.Select.Bind(() =&gt; Driver).Label("Driver").Listbox.Searchable().Clearable()[…],
///         Ui.DatePicker.Range.Bind(() =&gt; Period).Label("Period").WithPresets().Clearable(),
///     ];
///
///     Div[filters, filters.Badges, Ui.Table[…]]
///     </code>
///     <para>
///     Each control is bound to a page member with <c>[QueryParam]</c>, and the set binds it to the address instead:
///     a pick moves the address (replacing the history entry), the router writes the member and the page hears
///     <c>OnUpdated</c>, as for any move of its query. A filter applies as it is chosen — a text field once typing
///     pauses — so the panel has no Apply button. <see cref="Badges" /> says "Driver: Ada" for each filter in force,
///     with a button that takes it off, and <see cref="Active" /> counts them. A filter change takes the table the set
///     drives back to its first page, and leaves the sort.
///     </para>
///     <para>
///     The panel is a lazy flyout: its fields reach the browser only when it opens. Not a Flux component: the
///     owner's (2026-10-21). A control the set cannot read — a select of several answers — goes in a
///     <see cref="UiFilter" />.
///     </para>
/// </remarks>
public sealed partial class UiFilters : Component
{
    private static readonly UiPartMarker Marker = new("ui-filters");

    private static readonly UiPartMarker BadgesMarker = new("ui-filters-badges");

    private readonly int _instance = UiInstanceCounter.Next();

    private IEnumerable<Component?>? _readFrom;

    private List<UiFilterEntry> _entries = [];

    // The table it drives, the first Ui.Table.Of(…) built after it in this render, and the badge buttons it drew.
    private UiTable? _table;
    private readonly List<Element> _presses = [];

    // It hands its controls over each time it is drawn, which no cached render would do.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <summary>Runs when the reader opens the panel: where a page loads what its fields offer.</summary>
    public Callback OnOpen { get; set; }

    /// <summary>How many filters are in force.</summary>
    public int Active => Entries().Count(entry => entry.Active);

    /// <summary>
    ///     A badge for each filter in force, each with a button that takes it off, and "Clear all" from two on.
    ///     Nothing while no filter is in force. Place it where the list's filters should show: above a table.
    /// </summary>
    public Component? Badges
    {
        get
        {
            var active = Entries().Where(entry => entry.Active).ToList();
            if (active.Count == 0)
            {
                return null;
            }

            var clearAll = active.Count > 1
                ? Ui.Button.Ghost.Sm.Key("clear-all").OnClick(ClearAsync)
                : null;
            if (clearAll is not null)
            {
                _presses.Add(clearAll);
            }

            return Div.Class("flex flex-wrap items-center gap-2").Data(BadgesMarker.With(null))[
                active.Select(Badge),
                clearAll?[RaskStrings.Get(RaskString.FiltersClearAll, "Clear all")]
            ];
        }
    }

    // The set itself, not a Component: the page keeps it to place its Badges and read its Active.

    /// <summary>The controls it holds, in the order its panel stacks them.</summary>
    /// <param name="children">The controls, each bound to a member the address fills.</param>
    [OverloadResolutionPriority(1)]
    public new UiFilters this[params Component?[] children]
    {
        get
        {
            _ = base[children];
            return Waiting();
        }
    }

    /// <summary>The controls it holds, in the order its panel stacks them.</summary>
    /// <param name="children">The controls, each bound to a member the address fills.</param>
    [OverloadResolutionPriority(1)]
    public new UiFilters this[IEnumerable<Component?> children]
    {
        get
        {
            _ = base[children];
            return Waiting();
        }
    }

    /// <summary>The controls it holds, a projection among them, in the order its panel stacks them.</summary>
    /// <param name="children">The controls, each bound to a member the address fills.</param>
    public new UiFilters this[params object?[] children]
    {
        get
        {
            _ = base[children];
            return Waiting();
        }
    }

    /// <summary>The table the set drives, handed over as it is built: the first after the set.</summary>
    internal void Drives(UiTable table) => _table ??= table;

    // A new render: the table and the badge buttons are this render's to find again.
    private UiFilters Waiting()
    {
        _table = null;
        _presses.Clear();
        UiFilterTables.Wait(this);
        return this;
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        foreach (var field in (Children ?? []).OfType<IUiFilterField>())
        {
            field.Take(this);
        }

        var name = "ui-filters-" + _instance.ToString(CultureInfo.InvariantCulture);
        var controls = Controls(name);
        var active = Active;
        var panel = Ui.Modal.Name(name).Flyout().Lazy().Class("md:w-96")[
            Div.Class("space-y-6")[
                Ui.Heading.Level(2).Lg[RaskStrings.Get(RaskString.Filters, "Filters")],
                Div.Class("space-y-6").Aria("controls", controls)[Children ?? []],
                Div.Class("flex gap-2")[
                    Ui.Button.Ghost.Disabled(active == 0).Aria("controls", controls).OnClick(ClearAsync)[RaskStrings.Get(RaskString.FiltersClear, "Clear filters")],
                    Ui.Spacer,
                    Ui.ModalClose[Ui.Button.Primary[RaskStrings.Get(RaskString.FiltersClose, "Close")]]
                ]
            ]
        ];

        // One box, so a header row that spreads its children apart places the button and not the closed panel.
        return Div.Class("flex shrink-0 items-center").Data(Marker.With(null))[
            Ui.ModalTrigger.Name(name)[
                Ui.Button.Sm.Icon(Ui.IconName.Funnel)[
                    RaskStrings.Get(RaskString.Filters, "Filters"),
                    // The count as Flux draws one beside a label (its nav bar badge): 8px after the words, a 20px block.
                    active == 0 ? null : UiNavItemMarkup.Badge(active.ToString(CultureInfo.CurrentCulture), null, null, "ms-2 inline-block", marked: false)
                ]
            ],
            Context.Provide(new UiModalOpening(OnOpen), name)[panel]
        ];
    }

    private Component Badge(UiFilterEntry entry)
    {
        var remove = Ui.BadgeClose.AriaLabel(RaskStrings.Get(RaskString.FilterRemove, "Remove filter {0}", entry.Label)).OnClick(() => RemoveAsync(entry));
        _presses.Add(remove);

        return Ui.Badge.Sm.Key(entry.Key ?? entry.Label)[entry.Words, remove];
    }

    // The id of the table the set drives — its own, or one the set gives it — written as aria-controls on the badge
    // buttons the page drew, so a press there shows the table's skeleton at once (rask-skeleton.ts).
    private string? Controls(string name)
    {
        if (_table is not { } table)
        {
            return null;
        }

        if (table.Id is null)
        {
            table.Id = name + "-table";
            BuilderRuntime.MarkChanged(table);
        }

        foreach (var press in _presses)
        {
            press.Aria("controls", table.Id);
        }

        return table.Id;
    }

    // The set's view of its children, read once for each list it is handed: Badges and Active are read in the page's
    // render, and the set renders after it from the same list.
    private List<UiFilterEntry> Entries()
    {
        if (!ReferenceEquals(_readFrom, Children))
        {
            _readFrom = Children;
            _entries = Read(Children);
        }

        return _entries;
    }

    private static List<UiFilterEntry> Read(IEnumerable<Component?>? children)
    {
        var entries = new List<UiFilterEntry>();
        foreach (var child in children ?? [])
        {
            switch (child)
            {
                case IUiFilterField field:
                    entries.Add(field.Filter());
                    break;
                case IFormControl or IUiFormControl:
                    throw new InvalidOperationException(
                        $"Ui.Filters cannot keep a {child.GetType().Name.Split('`')[0]} in the address. Put it in a Ui.Filter, "
                        + "which says what it holds and how it is taken off: Ui.Filter.Label(\"Tags\").Shown(…).OnRemove(…)[…].");
            }
        }

        return entries;
    }

    /// <summary>
    ///     A filter change: written to the address in one move, with the list it drives taken back to its first page.
    /// </summary>
    /// <param name="filters">The keys the change writes; one with no value is taken out.</param>
    /// <returns>A pager of buttons being told it is on page one.</returns>
    internal Task Narrow(params KeyValuePair<string, string?>[] filters)
    {
        var pager = _table?.Paginate as UiPagination;
        KeyValuePair<string, string?>[] move = [.. filters, .. UiFilterFirstPage.Keys(pager)];
        if (move.Length > 0)
        {
            Go.With(move);
        }

        return UiFilterFirstPage.Turn(pager);
    }

    private async Task RemoveAsync(UiFilterEntry entry)
    {
        if (entry.Key is { } key)
        {
            await Narrow(new KeyValuePair<string, string?>(key, null)).ConfigureAwait(false);
            return;
        }

        await entry.OnRemove.Invoke().ConfigureAwait(false);
        await Narrow().ConfigureAwait(false);
    }

    // Every filter off in one move of the address: the keys the fields are kept under, and the list's page, and
    // nothing else the page keeps there, so its sort survives.
    private async Task ClearAsync()
    {
        var entries = Entries();
        foreach (var entry in entries.Where(entry => entry.Key is null && entry.Active))
        {
            await entry.OnRemove.Invoke().ConfigureAwait(false);
        }

        await Narrow([.. entries.Where(entry => entry.Key is not null).Select(entry => new KeyValuePair<string, string?>(entry.Key!, null))])
            .ConfigureAwait(false);
    }
}
