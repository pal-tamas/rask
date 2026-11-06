namespace Rask.Flux;

/// <summary>
///     The owner-approved departures from Flux in LOOK (flux-component SKILL.md "Owner-approved departures"), let go by
///     every check that compares the kit's computed styles with Flux's (parity, parity-modal, the open-list checks):
///     each names the node by where it sits, and only the named properties are let go there. A match is a predicate on
///     the kit's node and the tree above it; nothing of Flux's is asked.
/// </summary>
public static class Departed
{
    private sealed record Departure(Func<Example, MeasuredNode, bool> Matches, string[] Keys);

    // The markers ui.css's NATIVE FEEL block names (its first rule): a control's own root.
    private static readonly string[] ChromeRoots =
    [
        "data-ui-button", "data-ui-tab", "data-ui-navlist-item", "data-ui-navbar-items", "data-ui-navmenu-item", "data-ui-sidebar-item",
        "data-ui-sidebar-toggle", "data-ui-sidebar-collapse", "data-ui-sidebar-profile", "data-ui-sidebar-brand", "data-ui-breadcrumbs-item",
        "data-ui-accordion-heading", "data-ui-select-button", "data-ui-pillbox-trigger", "data-ui-color-picker-trigger",
        "data-ui-color-picker-button", "data-ui-date-picker-button", "data-ui-time-picker-button", "data-ui-datetime-picker-button",
        "data-ui-checkbox", "data-ui-radio", "data-ui-switch", "data-ui-toggle", "data-ui-checkbox-buttons", "data-ui-checkbox-pills",
        "data-ui-checkbox-cards", "data-ui-radio-buttons", "data-ui-radio-pills", "data-ui-radio-cards", "data-ui-radio-segmented", "data-ui-table-sortable", "data-ui-profile", "data-ui-brand",
        "data-ui-avatar",
    ];

    // …and the parts whose BUTTONS (or days) are chrome, not the part itself.
    private static readonly string[] ChromeWithin = ["data-ui-pagination", "data-ui-calendar", "data-rask-calendar", "data-ui-toast-dialog"];
    private static readonly string[] Pictured = ["data-ui-avatar", "data-ui-brand", "data-ui-sidebar-brand", "data-ui-flag"];
    private static readonly string[] Scrollers = ["data-ui-command-items", "data-ui-menu", "data-ui-popover"];

    private static readonly Departure[] Departures =
    [
        // 2026-10-14: Ui.Table's scroll frame — the box holding the <table> itself — is `relative`, so it holds what
        // its cells place absolutely.
        new((example, node) => example.ChildrenOf(node).Any(c => c.Tag == "table"), ["position"]),
        // 2026-11-02, NATIVE FEEL (ui.css): a control selects no text, waits for no second tap and opens no long-press
        // callout. `user-select` inherits in Blink, so what is inside a control computes it too.
        new((example, node) => example.WithAncestors(node).Any(n => Chrome(example, n)), ["userSelect", "webkitUserSelect", "touchAction", "webkitTouchCallout"]),
        // …a picture that is chrome does not drag, and a long press on it opens nothing.
        new((example, node) => node.Tag == "img" && example.WithAncestors(node).Any(n => Marked(n, Pictured)), ["webkitUserDrag", "webkitTouchCallout"]),
        new((_, node) => Marked(node, ["data-ui-icon"]), ["webkitTouchCallout"]),
        // …and an overlay's own scroller keeps its scroll: a modal's dialog, the command palette's list, a menu, a popover.
        new((example, node) => Marked(node, Scrollers) || (node.Tag == "dialog" && example.ParentOf(node) is { } parent && Marked(parent, ["data-ui-modal"])), ["overscrollBehaviorY"]),
        // …and the date picker's preset <select>, which is 16px below `sm` as every other field is, where Flux's is 14px —
        // its options with it, whose gap the browser draws in ems.
        new((example, node) => example.WithAncestors(node).Any(n => n.Tag == "select") && example.WithAncestors(node).Any(n => Marked(n, ["data-ui-date-picker"])), ["fontSize", "lineHeight", "gap"]),
    ];

    /// <summary>
    ///     2026-11-03, A FOCUSED FIELD (ui.css): the kit rings a focused field in the accent — no outline, the border and
    ///     an inset 1px ring in <c>--color-accent</c> — where Flux leaves the browser's own outline. parity-focus holds
    ///     these of its cases to that ring in every step where Flux's field draws one; a button, tab, checkbox, radio or
    ///     switch is not among them and keeps Flux's ring.
    /// </summary>
    public static readonly IReadOnlySet<string> FocusAccent = new HashSet<string>(StringComparer.Ordinal)
    {
        "input", "textarea", "select (native)", "select (listbox)", "date picker", "date picker (input)",
    };

    // …and what a FORCED :focus-visible may change differently on those fields (parity, parity-modal): Flux's
    // browser outline against the kit's border and inset ring in the accent; a typed date or time part's outline.
    private static readonly string[] FocusFields =
    [
        "data-ui-select-button", "data-ui-date-picker-button", "data-ui-time-picker-button",
        "data-ui-datetime-picker-button", "data-ui-color-picker-button", "data-ui-color-picker-popover-input", "data-ui-pillbox-trigger",
        // the inputs inside a ringed box, which draw nothing of their own
        "data-ui-color-picker-input", "data-ui-pillbox-input",
    ];

    private static readonly string[] FocusParts =
    [
        "data-ui-month-input", "data-ui-day-input", "data-ui-year-input", "data-ui-hour-input", "data-ui-minute-input",
        "data-ui-second-input", "data-ui-meridiem-input",
    ];

    private static readonly HashSet<string> Untyped = new(StringComparer.Ordinal) { "checkbox", "radio", "range", "file", "color", "hidden" };

    private static readonly string[] FocusKeys =
    [
        "outlineStyle", "outlineWidth", "outlineColor", "outlineOffset", "boxShadow",
        "borderTopColor", "borderRightColor", "borderBottomColor", "borderLeftColor",
    ];

    /// <summary>The style keys an owner-approved departure lets go on the kit's <paramref name="node" />, asked of it and the tree above it.</summary>
    public static List<string> From(Example example, MeasuredNode node) =>
        Departures.Where(d => d.Matches(example, node)).SelectMany(d => d.Keys).ToList();

    /// <summary>The style keys whose <c>:focus-visible</c> change may differ from Flux's on the kit's <paramref name="node" />: a field ringed in the accent.</summary>
    public static IReadOnlyList<string> FocusFrom(MeasuredNode node)
    {
        var field = (node.Tag is "input" or "textarea" or "select" && node.Has("data-ui-control") && !Untyped.Contains(node.Attr("type") ?? "\0"))
            || Marked(node, FocusFields) || Marked(node, FocusParts);
        return field ? FocusKeys : [];
    }

    private static bool Marked(MeasuredNode node, string[] names) => names.Any(node.Has);

    private static bool Chrome(Example example, MeasuredNode node)
    {
        if (Marked(node, ChromeRoots) || (node.Attr("role")?.StartsWith("menuitem", StringComparison.Ordinal) ?? false))
        {
            return true;
        }

        var within = (node.Tag == "button" || node.Attr("role") == "gridcell") && example.WithAncestors(node).Skip(1).Any(n => Marked(n, ChromeWithin));
        // A checkbox's, radio's or switch's label beside it in its field.
        var label = Marked(node, ["data-ui-label"]) && example.Nodes.Any(c => c.Parent == node.Parent && Marked(c, ["data-ui-checkbox", "data-ui-radio", "data-ui-switch"]));
        return within || label;
    }
}
