using Rask.Core.Components;
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
        new(Ui.TabsVariant.Default, Ui.TabsSize.Base, chosen: null, flagged: true, _ => Task.CompletedTask);

    private Component Tab(UiTabScope scope, string name, bool selected)
    {
        // Only a tab that names a panel is tied to one; a segmented row on its own has none.
        var group = Name is null ? null : Context.Get<UiTabGroupScope>();
        var pressed = selected ? "true" : "false";

        var button = Button
            .Id(group?.TabId(name))
            .Class(TabClass(scope, selected))
            .Data((selected ? SelectedMarker : Marker).With(null))
            .Role("tab")
            // Roving tabindex: only the selected tab is a tab stop, so Tab leaves the row for the panel
            // rather than walking every remaining tab. The arrows move between them instead.
            .TabIndex(selected ? 0 : -1)
            .Type(ButtonType.Button)
            .Disabled(Disabled == true)
            .OnClick(() => PickAsync(scope, name));

        button = group is null
            ? button.Aria("selected", pressed)
            : button.Aria(("selected", pressed), ("controls", group.PanelId(name)));

        return button[Glyph(Icon, scope, selected), Children ?? [], Glyph(IconTrailing, scope, selected)];
    }

    private async Task PickAsync(UiTabScope scope, string name)
    {
        await scope.Select(name).ConfigureAwait(false);
        await OnClick.Invoke().ConfigureAwait(false);
    }

    private Component ActionButton(UiTabScope scope) =>
        Button
            .Class(TabClass(scope, selected: false))
            .Data(Marker.With(null))
            .Type(ButtonType.Button)
            .Disabled(Disabled == true)
            .OnClick(() => OnClick.Invoke().AsTask())[
            Glyph(Icon, scope, selected: false), Children ?? [], Glyph(IconTrailing, scope, selected: false)
        ];

    // 20px whatever the drawing: the outline beside an underlined or pill tab, the mini inside a segment —
    // where the icon of a tab that is not selected stays a shade quieter than its label, hovered or not.
    private UiIcon? Glyph(Ui.IconName? name, UiTabScope scope, bool selected)
    {
        if (name is not { } icon)
        {
            return null;
        }

        var segmented = scope.Variant == Ui.TabsVariant.Segmented;
        return Ui.Icon
            .Name(icon)
            .Variant(IconVariant ?? (segmented ? Ui.IconVariant.Mini : Ui.IconVariant.Outline))
            .Class(segmented && !selected ? "size-5 text-zinc-500 dark:text-zinc-400" : "size-5");
    }

    private string TabClass(UiTabScope scope, bool selected)
    {
        var segment = (Size ?? scope.Size) == Ui.TabsSize.Sm ? SegmentSm : Segment;
        var (shape, state) = (scope.Variant, selected, Accent != false) switch
        {
            (Ui.TabsVariant.Segmented, true, _) => (segment, SegmentOn),
            (Ui.TabsVariant.Segmented, false, _) => (segment, SegmentOff),
            (Ui.TabsVariant.Pills, true, true) => (Pill, PillOn),
            (Ui.TabsVariant.Pills, true, false) => (Pill, PillOnBase),
            (Ui.TabsVariant.Pills, false, _) => (Pill, PillOff),
            (_, true, true) => (Underlined, UnderlinedOn),
            (_, true, false) => (Underlined, UnderlinedOnBase),
            _ => (Underlined, UnderlinedOff),
        };

        return UiClass.Compose(shape, state, Unavailable, Class);
    }

    // A 2px underline that sits ON the row's hairline (-mb-px), in the accent's readable shade.
    private const string Underlined = "flex items-center gap-2 px-2 -mb-px border-b-2 text-sm font-medium whitespace-nowrap";
    private const string UnderlinedOn = "border-fx-accent-content text-fx-accent-content";
    private const string UnderlinedOnBase = "border-zinc-800 text-zinc-800 dark:border-white dark:text-white";
    private const string UnderlinedOff =
        "border-transparent text-zinc-400 hover:text-zinc-800 dark:text-white/50 dark:hover:text-white";

    // The selected segment is the base colour whatever the accent: white lifted out of the track.
    private const string Segment =
        "flex flex-1 items-center justify-center gap-2 px-4 rounded-md text-sm font-medium whitespace-nowrap";
    private const string SegmentSm =
        "flex flex-1 items-center justify-center gap-2 px-3 rounded-md text-sm font-medium whitespace-nowrap";
    private const string SegmentOn = "bg-white text-zinc-800 shadow-xs dark:bg-white/20 dark:text-white";
    private const string SegmentOff = "text-zinc-600 hover:text-zinc-800 dark:text-white/70 dark:hover:text-white";

    private const string Pill = "flex items-center gap-2 px-3 rounded-full text-sm font-medium whitespace-nowrap";
    private const string PillOn = "bg-fx-accent text-fx-accent-foreground";
    private const string PillOnBase = "bg-zinc-800 text-white dark:bg-white dark:text-zinc-800";
    private const string PillOff =
        "bg-zinc-800/5 text-zinc-600 hover:bg-zinc-800/10 hover:text-zinc-800 "
        + "dark:bg-white/5 dark:text-white/70 dark:hover:bg-white/10 dark:hover:text-white";

    private const string Unavailable = "disabled:pointer-events-none disabled:opacity-50 dark:disabled:opacity-75";
}
