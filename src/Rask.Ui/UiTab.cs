using Rask.Core.Components;
using Rask.Core.Forms;
using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     Flux's <c>flux:tab</c>: one tab in a <see cref="UiTabs" />. Its children are its label.
/// </summary>
/// <remarks>
///     <para>
///     A <c>&lt;button role="tab"&gt;</c>. <see cref="Name" /> is what the row's value becomes when it is
///     selected, and what ties it to the <see cref="UiTabPanel" /> of the same name in a
///     <see cref="UiTabGroup" />. A tab without a name is known by its place in the row: <c>"0"</c>,
///     <c>"1"</c>…
///     </para>
///     <para>
///     A press selects it in the browser and sends the page nothing; what paints it selected reads
///     <c>data-selected</c>, as Flux's does. <see cref="OnClick" /> is the page's own, and is all a press sends.
///     </para>
///     <para>
///     <see cref="Action" /> makes it a plain button that sits in the row and selects nothing — "Add tab".
///     It is never a link, as Flux's is not: a row that navigates reads its <c>OnChange</c>.
///     </para>
/// </remarks>
public sealed partial class UiTab : Component
{
    private static readonly UiPartMarker Marker = new("ui-tab");
    private static readonly UiPartMarker SelectedMarker = Marker.And("selected");

    /// <summary>What the row's value is when this tab is selected, and the panel it shows.</summary>
    public string? Name { get; set; }

    /// <summary>An icon before the label.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>An icon after the label.</summary>
    public Ui.IconName? IconTrailing { get; set; }

    /// <summary>
    ///     Which drawing of the icons. Unset, it is the outline — or the mini in a segmented row — at 20px.
    /// </summary>
    public Ui.IconVariant? IconVariant { get; set; }

    /// <summary>
    ///     Selects this tab until one is chosen. The row's own value, when it has one, decides instead.
    /// </summary>
    public bool? Selected { get; set; }

    /// <summary>
    ///     Makes it a button in the row rather than a tab: it runs <see cref="OnClick" />, selects nothing and
    ///     the arrow keys pass over it.
    /// </summary>
    public bool? Action { get; set; }

    /// <summary>
    ///     Whether the selected tab is painted in the accent colour. On unless this says <c>false</c>, which
    ///     paints it in the base colour instead.
    /// </summary>
    public bool? Accent { get; set; }

    /// <summary>This tab's size in a segmented row. Unset, it is the row's.</summary>
    public Ui.TabsSize? Size { get; set; }

    /// <summary>Dims it and takes it out of reach of the pointer and of the arrow keys.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Runs when it is pressed — after it is selected, or instead for an <see cref="Action" />.</summary>
    public Callback OnClick { get; set; }

    public string? Class { get; set; }

    // A tab takes its place in the row as it renders, and the render cache cannot see that.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var scope = Context.Get<UiTabScope>() ?? Alone();
        if (Action == true)
        {
            return ActionButton(scope);
        }

        var (name, selected) = scope.Place(Name, Selected == true, first: Disabled != true);

        return Tab(scope, name, selected);
    }

    // Outside a Ui.Tabs there is no row to ask: the tab draws as the default variant, is selected only if it
    // says so, and pressing it selects nothing.
    private static UiTabScope Alone() =>
        new(Ui.TabsVariant.Default, Ui.TabsSize.Base, chosen: null, flagged: true);

    private Component Tab(UiTabScope scope, string name, bool selected)
    {
        // Only a tab that names a panel is tied to one; a segmented row on its own has none.
        var group = Name is null ? null : Context.Get<UiTabGroupScope>();
        var pressed = selected ? "true" : "false";

        var button = Button
            .Id(group?.TabId(name))
            .Class(TabClass(scope))
            .Data((selected ? SelectedMarker : Marker).With(null, "rask-tab", name))
            .Role("tab")
            // Roving tabindex: only the selected tab is a tab stop, so Tab leaves the row for the panel
            // rather than walking every remaining tab. The arrows move between them instead.
            .TabIndex(selected ? 0 : -1)
            .Type(ButtonType.Button)
            .Disabled(Disabled == true);

        // Selecting is the runtime's (rask-tabs.ts): a press is the page's only when the page listens.
        if (OnClick.HasValue)
        {
            button = button.OnClick(() => OnClick.Invoke().AsTask());
        }

        button = group is null
            ? button.Aria("selected", pressed)
            : button.Aria(("selected", pressed), ("controls", group.PanelId(name)));

        return button[Glyph(Icon, scope), Children ?? [], Glyph(IconTrailing, scope)];
    }

    private Component ActionButton(UiTabScope scope) =>
        Button
            .Class(TabClass(scope))
            .Data(Marker.With(null))
            .Type(ButtonType.Button)
            .Disabled(Disabled == true)
            .OnClick(() => OnClick.Invoke().AsTask())[
            Glyph(Icon, scope), Children ?? [], Glyph(IconTrailing, scope)
        ];

    // 20px whatever the drawing: the outline beside an underlined or pill tab, the mini inside a segment —
    // where the icon of a tab that is not selected stays a shade quieter than its label, hovered or not.
    private UiIcon? Glyph(Ui.IconName? name, UiTabScope scope)
    {
        if (name is not { } icon)
        {
            return null;
        }

        var segmented = scope.Variant == Ui.TabsVariant.Segmented;
        return Ui.Icon
            .Name(icon)
            .Variant(IconVariant ?? (segmented ? Ui.IconVariant.Mini : Ui.IconVariant.Outline))
            .Class(segmented ? SegmentIcon : "size-5");
    }

    // One class list whichever tab is selected: the selected one is painted from `data-selected`, which the
    // runtime moves as a tab is chosen.
    private string TabClass(UiTabScope scope)
    {
        var segment = (Size ?? scope.Size) == Ui.TabsSize.Sm ? SegmentSm : Segment;
        var (shape, paint) = (scope.Variant, Accent != false) switch
        {
            (Ui.TabsVariant.Segmented, _) => (segment, SegmentPaint),
            (Ui.TabsVariant.Pills, true) => (Pill, PillPaint),
            (Ui.TabsVariant.Pills, false) => (Pill, PillPaintBase),
            (_, true) => (Underlined, UnderlinedPaint),
            _ => (Underlined, UnderlinedPaintBase),
        };

        return UiClass.Compose(shape, paint, Unavailable, Class);
    }

    // A 2px underline that sits ON the row's hairline (-mb-px), in the accent's readable shade.
    private const string Underlined =
        "flex items-center gap-2 px-2 -mb-px border-b-2 border-transparent text-sm font-medium whitespace-nowrap "
        + "text-zinc-400 hover:text-zinc-800 dark:text-white/50 dark:hover:text-white";
    private const string UnderlinedPaint =
        "data-selected:border-fx-accent-content data-selected:text-fx-accent-content "
        + "dark:data-selected:border-fx-accent-content dark:data-selected:text-fx-accent-content "
        + "hover:data-selected:text-fx-accent-content dark:hover:data-selected:text-fx-accent-content";
    private const string UnderlinedPaintBase =
        "data-selected:border-zinc-800 data-selected:text-zinc-800 "
        + "dark:data-selected:border-white dark:data-selected:text-white";

    // The selected segment is the base colour whatever the accent: white lifted out of the track.
    private const string Segment =
        "flex flex-1 items-center justify-center gap-2 px-4 rounded-md text-sm font-medium whitespace-nowrap";
    private const string SegmentSm =
        "flex flex-1 items-center justify-center gap-2 px-3 rounded-md text-sm font-medium whitespace-nowrap";
    private const string SegmentPaint =
        "text-zinc-600 hover:text-zinc-800 dark:text-white/70 dark:hover:text-white "
        + "data-selected:bg-white data-selected:text-zinc-800 data-selected:shadow-xs "
        + "dark:data-selected:bg-white/20 dark:data-selected:text-white";
    private const string SegmentIcon =
        "size-5 text-zinc-500 dark:text-zinc-400 "
        + "[[data-ui-tab][data-selected]_&]:text-zinc-800 dark:[[data-ui-tab][data-selected]_&]:text-white";

    private const string Pill =
        "flex items-center gap-2 px-3 rounded-full text-sm font-medium whitespace-nowrap "
        + "bg-zinc-800/5 text-zinc-600 hover:bg-zinc-800/10 hover:text-zinc-800 "
        + "dark:bg-white/5 dark:text-white/70 dark:hover:bg-white/10 dark:hover:text-white";
    private const string PillPaint =
        "data-selected:bg-fx-accent hover:data-selected:bg-fx-accent dark:data-selected:bg-fx-accent "
        + "dark:hover:data-selected:bg-fx-accent data-selected:text-fx-accent-foreground "
        + "hover:data-selected:text-fx-accent-foreground dark:data-selected:text-fx-accent-foreground "
        + "dark:hover:data-selected:text-fx-accent-foreground";
    private const string PillPaintBase =
        "data-selected:bg-zinc-800 hover:data-selected:bg-zinc-800 data-selected:text-white "
        + "hover:data-selected:text-white dark:data-selected:bg-white dark:hover:data-selected:bg-white "
        + "dark:data-selected:text-zinc-800 dark:hover:data-selected:text-zinc-800";

    private const string Unavailable = "disabled:pointer-events-none disabled:opacity-50 dark:disabled:opacity-75";
}
