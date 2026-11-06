using System.Text.RegularExpressions;

namespace Rask.Flux;

/// <summary>What is measured of every node, and the cursor departure the comparisons let go.</summary>
public static partial class Pressables
{
    /// <summary>The computed styles recorded for every node and pseudo-element, in this order.</summary>
    public static readonly string[] Styles =
    [
        "display", "position", "boxSizing", "width", "height", "minWidth", "minHeight", "maxWidth",
        "paddingTop", "paddingRight", "paddingBottom", "paddingLeft",
        "marginTop", "marginRight", "marginBottom", "marginLeft", "gap",
        "flexDirection", "alignItems", "justifyContent", "flexGrow", "flexShrink", "gridTemplateColumns",
        "fontFamily", "fontSize", "lineHeight", "fontWeight", "letterSpacing", "textAlign", "textTransform",
        "textDecorationLine", "whiteSpace", "color", "backgroundColor", "backgroundImage", "opacity",
        "borderTopWidth", "borderRightWidth", "borderBottomWidth", "borderLeftWidth", "borderTopStyle",
        "borderTopColor", "borderRightColor", "borderBottomColor", "borderLeftColor",
        "borderTopLeftRadius", "borderTopRightRadius", "borderBottomRightRadius", "borderBottomLeftRadius",
        "boxShadow", "outlineStyle", "outlineWidth", "outlineColor", "outlineOffset", "cursor",
        "overflowX", "overflowY", "zIndex", "transform", "transitionProperty", "transitionDuration",
        "fill", "stroke", "strokeWidth", "backdropFilter",
        // What a touch does: selects, zooms, scrolls past, opens a callout, drags (parity's DEPARTED, NATIVE FEEL).
        "userSelect", "webkitUserSelect", "touchAction", "overscrollBehaviorY", "webkitTouchCallout", "webkitUserDrag",
    ];

    // What the kit shows the hand over — an owner-approved departure from Flux (2026-11-02, flux-component SKILL.md
    // "Owner-approved departures"), whose controls keep the arrow. The kit sheet's rule (src/Rask.Ui/Styles/ui.css),
    // plus the kit's own parts that are pressed but are no <button>: a picker's clear as a <div>, a pillbox trigger, a
    // select's create row, a file upload (the <label> round its file input; the arrow there while it is disabled).
    // Measured in the page as `node.pressable` — the element, or what it sits inside, is one of these and can be pressed now.
    private const string PressableParts = ":is(button, [role=button], [role=menuitem], [role=menuitemcheckbox], [role=menuitemradio], [role=option], "
        + "[role=tab], [role=checkbox], [role=radio], [role=switch], summary, select, label[for], "
        + "label:has(input:is([type=checkbox], [type=radio])), "
        + "input:is([type=checkbox], [type=radio], [type=file], [type=range], [type=color], [type=submit], [type=button], [type=reset]), "
        + "[data-ui-button], [data-ui-option-create], [role=combobox]:not(input), [data-ui-file-upload]:not(:has(input:disabled)))";

    private const string HeldStates = ":is(:disabled, [aria-disabled=true], [data-disabled], [readonly], [aria-readonly=true], [data-loading], [aria-busy=true])";

    /// <summary>What can be pressed now.</summary>
    public const string Pressable = $"{PressableParts}:not({HeldStates})";

    /// <summary>
    ///     …and what could be pressed but is held now (disabled, read-only, busy): the kit shows the ARROW there, the same
    ///     departure, also where Flux keeps the hand (its disabled colour swatch). Measured as <c>node.pressable === 'held'</c>.
    /// </summary>
    public const string Held = $"{PressableParts}{HeldStates}";

    /// <summary>
    ///     Whether <c>cursor</c> may differ between Flux's node and the kit's: only where the kit computes the hand on a
    ///     pressable node (<paramref name="pressable" />, measured in the page with <see cref="Pressable" />) and Flux the
    ///     arrow (<c>default</c>, or <c>auto</c>, which is the arrow over anything but text). Any other cursor difference
    ///     is still a difference — the departure is that, and no wider. <paramref name="pressable" /> is
    ///     <c>"held"</c>, <c>"true"</c> or anything else for false.
    /// </summary>
    public static bool CursorDeparted(string? flux, string? rask, string pressable)
    {
        if (pressable == "held")
        {
            return rask == "default" && flux == "pointer";
        }

        return pressable == "true" && rask == "pointer" && flux is "default" or "auto";
    }

    /// <summary>
    ///     Whether two nodes show the same cursor though they compute different keywords: <c>auto</c> IS the arrow over
    ///     anything but text, so on a node that draws no text of its own <c>auto</c> and <c>default</c> are one cursor.
    ///     Flux's disabled calendar arrow is its own element and stays <c>auto</c>; the kit's is a disabled
    ///     <c>&lt;button&gt;</c>, whose arrow is <c>default</c>.
    /// </summary>
    public static bool SameArrow(MeasuredNode a, MeasuredNode b)
    {
        static bool Arrow(string? cursor) => cursor is "auto" or "default";
        return a.Text.Length == 0 && b.Text.Length == 0 && Arrow(a.Style("cursor")) && Arrow(b.Style("cursor"));
    }

    /// <summary>
    ///     The same, for a transcript's two lines of <c>key=value;…</c> (parity-editor): the kit's line says
    ///     <c>cursor=pointer</c> and ends <c>;pressable</c>, Flux's says the arrow, and with that put back they are the same line.
    /// </summary>
    public static bool CursorDepartedLine(string? flux, string? rask)
    {
        if (flux is null || rask is null || !rask.EndsWith(";pressable", StringComparison.Ordinal))
        {
            return false;
        }

        var theirs = CursorWord().Match(flux) is { Success: true } m ? m.Groups[1].Value : null;
        if (!CursorDeparted(theirs, "pointer", "true"))
        {
            return false;
        }

        var mine = rask[..^";pressable".Length];
        var put = CursorPointer().Replace(mine, match => match.Groups[1].Value + theirs, 1);
        return put == flux;
    }

    [GeneratedRegex("(?:^|;| )cursor=([a-z-]+)")]
    private static partial Regex CursorWord();

    [GeneratedRegex("((?:^|;| )cursor=)pointer")]
    private static partial Regex CursorPointer();
}
