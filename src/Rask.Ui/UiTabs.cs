using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     Flux's <c>flux:tabs</c>: the row of <see cref="UiTab" />s, and the owner of which one is selected.
/// </summary>
/// <remarks>
///     <para>
///     The selected tab's <c>Name</c> is a value like any other control's: <c>.Bind(() =&gt; Tab)</c> two-way
///     binds it to a property, <c>.Value(_tab).OnChange(t =&gt; _tab = t)</c> leaves it with the page, and with
///     neither the row keeps track itself, starting on the tab that says <c>Selected</c> or else the first.
///     </para>
///     <para>
///     Inside a <see cref="UiTabGroup" /> it switches the panels beside it. On its own — a segmented
///     "List / Board / Timeline" — it is a choice the page reads from <c>OnChange</c>.
///     </para>
///     <para>
///     The keyboard is the runtime's, for any <c>role="tablist"</c>: the arrow keys move to the next tab that
///     is not disabled, wrapping at the ends, and select it as they go. Only the selected tab is a tab stop.
///     </para>
/// </remarks>
public sealed partial class UiTabs : Component
{
    private static readonly UiPartMarker Marker = new("ui-tabs");

    private string? _selected;

    /// <summary>How the tabs are drawn. A row on a hairline unless this says otherwise.</summary>
    public Ui.TabsVariant? Variant { get; set; }

    /// <summary>How large the tabs are. Only <see cref="Ui.TabsVariant.Segmented" /> has a second size.</summary>
    public Ui.TabsSize? Size { get; set; }

    /// <summary>Scrolls the row sideways instead of letting it push the page wider than a phone.</summary>
    public bool? Scrollable { get; set; }

    /// <summary>Whether a scrollable row shows its scrollbar.</summary>
    public Ui.TabsScrollbar? ScrollableScrollbar { get; set; }

    /// <summary>
    ///     Fades a scrollable row's trailing edge while there are tabs past it, so the cut-off reads as "more".
    /// </summary>
    public bool? ScrollableFade { get; set; }

    public string? Class { get; set; }

    /// <summary>The selected tab's name, when the page holds it. Unset, the row keeps track itself.</summary>
    public string? Value { get; set; }

    /// <summary>Two-way binds the selected tab's name to a property: <c>.Bind(() =&gt; Tab)</c>.</summary>
    public Expression<Func<string>>? Bind { get; set; }

    /// <summary>Runs when a tab is selected, with its name.</summary>
    public Callback<string> OnChange { get; set; }

    // What the row remembers is a field, and the scope is filled by the tabs as they render.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var accessor = Bind is { } bind ? ExpressionAccessor.Parse(bind) : null;
        var held = accessor is null ? Value : accessor.Getter() as string;
        var variant = Variant ?? Ui.TabsVariant.Default;
        var chosen = string.IsNullOrEmpty(held) ? _selected : held;
        var scope = new UiTabScope(
            variant,
            Size ?? Ui.TabsSize.Base,
            chosen,
            chosen is null && AnyTabSaysSelected(),
            name => SelectAsync(name, accessor));

        if (Context.Get<UiTabGroupScope>() is { } group)
        {
            group.Tabs = scope;
        }

        var scrolls = Scrollable == true;
        var list = Div
            .Class(UiClass.Compose(ListClass(variant, scope.Size, scrolls), Class))
            .Data(Marker.With(null))
            .Role("tablist")[
            Context.Provide(scope)[Children ?? []]
        ];

        return scrolls ? ScrollArea(list, variant) : list;
    }

    private async Task SelectAsync(string name, ExpressionAccessor.Accessor? accessor)
    {
        // Remembered even when the page holds the value: a page that stops passing one keeps the tab it was on.
        _selected = name;
        if (accessor is not null)
        {
            accessor.Setter(name);
            var context = BindingHelpers.ResolveBindingContext(accessor.Target);
            await BindingHelpers.NotifyAndValidateField(context, accessor.Field).ConfigureAwait(false);
        }

        await OnChange.Invoke(name).ConfigureAwait(false);
    }

    // A later tab's `Selected` has to be known before the first tab renders, or the first would claim the row.
    private bool AnyTabSaysSelected() => (Children ?? []).Any(child => child is UiTab { Selected: true });

    // The hairline moves off the tablist onto a sibling: the tablist is as wide as its tabs, and the line
    // has to stay the width of the box they scroll in.
    private Component ScrollArea(Component list, Ui.TabsVariant variant) =>
        // Focusable from script and not a tab stop, as Flux's wrapper is.
        Div.Class("relative").TabIndex(-1)[
            variant == Ui.TabsVariant.Default
                ? Div.Class("absolute inset-x-0 bottom-0 h-px bg-zinc-800/10 dark:bg-white/20")
                : null,
            Div.Class(ScrollClass(ScrollableScrollbar == Ui.TabsScrollbar.Hide, ScrollableFade == true))[
                Div.Class("min-w-full shrink-0")[list]
            ]
        ];

    private static string ScrollClass(bool hidesScrollbar, bool fades) => (hidesScrollbar, fades) switch
    {
        (false, false) => "relative flex overflow-auto",
        (false, true) => "relative flex overflow-auto ui-tabs-fade",
        (true, false) => "relative flex overflow-auto [scrollbar-width:none] [&::-webkit-scrollbar]:hidden",
        (true, true) => "relative flex overflow-auto ui-tabs-fade [scrollbar-width:none] [&::-webkit-scrollbar]:hidden",
    };

    private static string ListClass(Ui.TabsVariant variant, Ui.TabsSize size, bool scrolls) => (variant, size) switch
    {
        (Ui.TabsVariant.Segmented, Ui.TabsSize.Sm) => "inline-flex h-8.5 p-[3px] rounded-lg cursor-default bg-zinc-800/5 dark:bg-white/10",
        (Ui.TabsVariant.Segmented, _) => "inline-flex h-10 p-1 rounded-lg cursor-default bg-zinc-800/5 dark:bg-white/10",
        (Ui.TabsVariant.Pills, _) => "flex h-8 gap-4 cursor-default",
        _ when scrolls => "flex h-10 gap-4 cursor-default border-b border-transparent",
        _ => "flex h-10 gap-4 cursor-default border-b border-zinc-800/10 dark:border-white/20",
    };
}
