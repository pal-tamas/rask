using System.Text;

namespace Rask;

/// <summary>What every row of a <see cref="UiMenu" /> is made of, measured off Flux UI's open menus.</summary>
/// <remarks>
///     A menu's row is highlighted one way, as Flux's is: <c>data-active</c>, written for the pointer and for the
///     keyboard alike by the runtime, in the browser (<c>data-rask-menu-pointer</c>, <c>data-rask-menu-cursor</c>)
///     — never by a render. There is no <c>:hover</c> on it: two highlights are two lit rows whenever the
///     pointer rests on one and the arrows move to another. A navigation menu's link has no cursor, and keeps
///     <c>:hover</c>.
/// </remarks>
internal static class UiMenuRow
{
    /// <summary>The popup itself, and a submenu's flyout: Flux draws them alike.</summary>
    internal const string Panel = Box + " focus:outline-hidden";

    /// <summary>The same box around a <see cref="UiNavmenu" />, whose links keep the browser's focus ring.</summary>
    /// <remarks>
    ///     Its ink is stated, as a popover's is by the browser: measured on Flux, a menu opened from a greyed
    ///     breadcrumb is black on white (white on zinc-700 in dark), not the grey of the crumb it hangs from.
    /// </remarks>
    internal const string Box =
        "min-w-48 overflow-auto rounded-lg border border-zinc-200 bg-white p-[.3125rem] text-black shadow-xs "
        + "dark:border-zinc-600 dark:bg-zinc-700 dark:text-white";

    /// <summary>A menu row's leading icon: quieter than the words until the row is lit.</summary>
    internal const string LeadingIcon = "me-2 " + Mark;

    // The check of a checkbox or radio row: the same ink, with no margin — its room is the 28px around it.
    private const string Mark = "text-zinc-400 group-data-active/row:text-current dark:text-white/60";

    // A navigation menu's link is lit by the pointer alone.
    private const string LinkLeadingIcon =
        "me-2 text-zinc-400 group-hover/row:text-current group-data-active/row:text-current dark:text-white/60";

    /// <summary>A trailing icon. It follows the pointer and not the cursor, as a submenu's chevron does in Flux.</summary>
    internal const string TrailingIcon = "ms-auto text-zinc-400 group-hover/row:text-current";

    // Flux's `data-flux-menu-item-icon`, beside the icon's own marks: a row's leading icon and its check.
    private static readonly Dictionary<string, string?> ItemIcon = UiIcon.MarksWith("data-ui-menu-item-icon");

    private static readonly Dictionary<string, string?> LinkMark = UiIcon.MarksWith("data-navmenu-icon");

    private const string ChevronLtr = TrailingIcon + " rtl:hidden";

    private const string ChevronRtl = TrailingIcon + " hidden rtl:inline";

    /// <summary>The words at the end of a row: a shortcut, a count.</summary>
    internal const string Trailing = "ms-auto text-xs text-zinc-400";

    private const string Row =
        "group/row flex w-full items-center rounded-md px-2 py-1.5 text-start text-sm font-medium "
        + "disabled:pointer-events-none disabled:opacity-50 aria-disabled:pointer-events-none aria-disabled:opacity-50 "
        + "text-zinc-800 dark:text-white";

    private const string Lit = " data-active:bg-zinc-50 dark:data-active:bg-zinc-600";

    private const string LitDanger =
        " data-active:bg-red-50 data-active:text-red-600 dark:data-active:bg-red-400/20 dark:data-active:text-red-400";

    private const string Plain = Row + " hover:bg-zinc-50 dark:hover:bg-zinc-600" + Lit;

    private const string Danger =
        Row + " hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-400/20 dark:hover:text-red-400" + LitDanger;

    // A menu's row is focused by the cursor, and the highlight is how it shows: no ring on top of it.
    private const string PlainItem = Row + Lit + " focus:outline-hidden";

    private const string DangerItem = Row + LitDanger + " focus:outline-hidden";

    /// <summary>A menu row's own classes.</summary>
    internal static string Classes(Ui.MenuItemVariant? variant) =>
        variant == Ui.MenuItemVariant.Danger ? DangerItem : PlainItem;

    /// <summary>A navigation menu's link: the same row, with the browser's focus ring left on it.</summary>
    internal static string LinkClasses(Ui.MenuItemVariant? variant) => variant == Ui.MenuItemVariant.Danger ? Danger : Plain;

    /// <summary>The icon at the start of a row. Flux draws a menu's icons in the 20px set unless told otherwise.</summary>
    internal static Component Icon(Ui.IconName name, Ui.IconVariant? variant) =>
        UiIcon.Marked(ItemIcon, name, variant ?? Ui.IconVariant.Mini, LeadingIcon);

    /// <summary>A navigation menu row's icon: the same drawing under the mark Flux gives it there, <c>data-navmenu-icon</c>.</summary>
    internal static Component LinkIcon(Ui.IconName name, Ui.IconVariant? variant) =>
        UiIcon.Marked(LinkMark, name, variant ?? Ui.IconVariant.Mini, LinkLeadingIcon);

    /// <summary>The icon at the end of a row.</summary>
    internal static Component IconTrailing(Ui.IconName name, Ui.IconVariant? variant) =>
        Ui.Icon.Name(name).Variant(variant ?? Ui.IconVariant.Mini).Class(TrailingIcon);

    /// <summary>
    ///     A submenu row's chevron: the one that points the way the flyout opens, and its mirror for a
    ///     right-to-left page.
    /// </summary>
    internal static Component Chevrons() =>
    [
        Ui.Icon.Name(Ui.IconName.ChevronRight).Variant(Ui.IconVariant.Mini).Class(ChevronLtr),
        Ui.Icon.Name(Ui.IconName.ChevronLeft).Variant(Ui.IconVariant.Mini).Class(ChevronRtl)
    ];

    /// <summary>
    ///     The room an icon would take, in a row that has none. Hidden until a sibling row has an icon — the
    ///     stylesheet shows it then — so every row's words start at the same place.
    /// </summary>
    internal static Component Indent() => Markup.Div.Class("w-7").Data("indent", "");

    /// <summary>A checkbox or radio row's mark: always the icon's width, drawn only when the row is checked.</summary>
    internal static Component Check(bool on) =>
        Markup.Div.Class("w-7")[
            Markup.Div.Class(on ? null : "hidden")[
                UiIcon.Marked(ItemIcon, Ui.IconName.Check, Ui.IconVariant.Mini, Mark)
            ]
        ];

    /// <summary>The <c>data-*</c> a checkbox or radio row starts from: it always has the mark's room, and says when it is checked.</summary>
    internal static Dictionary<string, string?> Checkable(bool on, bool keepOpen)
    {
        var data = new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-menu-item-has-icon"] = "" };
        if (on)
        {
            data["checked"] = "";
        }

        if (keepOpen)
        {
            data["rask-keep-open"] = "";
        }

        return data;
    }

    /// <summary>A row's words, for type-ahead: the text among its children, markup stripped.</summary>
    internal static string Label(IEnumerable<Component?>? children)
    {
        var words = new StringBuilder();
        Collect(children, words);
        return words.ToString().Trim();
    }

    /// <summary>
    ///     Makes <paramref name="element" /> the menu row at <paramref name="ordinal" />: its id and role, the
    ///     roving tab stop, and the <c>data-*</c> hooks — Flux's markers, the cursor, a pick that keeps the menu
    ///     open, a checked row.
    /// </summary>
    internal static T Decorate<T>(
        T element,
        UiMenuLevel? level,
        int ordinal,
        string role,
        string marker,
        Dictionary<string, string?> aria,
        Dictionary<string, string?> data)
        where T : Element
    {
        data[marker] = "";

        // Only where the PAGE keeps the cursor. A menu's rows are all rendered unlit, with no tab stop: which one
        // is lit and which has focus is the runtime's, written and held in the browser (rask-menu.ts).
        var active = level is not null && ordinal == level.Scope.Active;
        if (active)
        {
            data["active"] = "";
        }

        // Chain steps, not property writes: a chain-built element renders what its chain was given.
        element = element.Role(role).TabIndex(active ? 0 : -1).Aria(aria).Data(data);
        return level is null ? element : element.Id(level.Scope.ItemId(ordinal));
    }

    private static void Collect(IEnumerable<Component?>? children, StringBuilder words)
    {
        foreach (var child in children ?? [])
        {
            if (child is Text text)
            {
                words.Append(text.Value);
            }
            else if (child is not null)
            {
                Collect(child.Children, words);
            }
        }
    }
}
