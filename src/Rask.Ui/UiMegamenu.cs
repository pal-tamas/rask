namespace Rask.Ui;

/// <summary>
/// A navigation bar whose entries open full panels beneath it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The browser opens this one, not C#</b> — the same story as <see cref="UiFab" />, for a different
/// mechanism. daisyUI builds the megamenu on the native popover API: each trigger is a
/// <c>popovertarget</c> and each panel a <c>[popover]</c>, and the open state is the browser's
/// <c>:popover-open</c>. There is no <c>megamenu-open</c> class to write, so nothing a page sets can
/// force it. <c>megamenu-active</c> is the sliding highlight, not the state.
/// </para>
/// <para>
/// That is a good trade here rather than a reluctant one. The browser supplies the top layer, Escape,
/// light-dismiss and focus containment, and the whole thing works on a prerendered page with no runtime
/// booted — which for a site's main navigation, the first thing a reader touches and the last thing
/// that should wait for a bundle, is worth more than programmatic control.
/// </para>
/// <para>
/// daisyUI positions the panels with CSS anchor positioning keyed on <c>:nth-of-type</c>, so the
/// triggers must be siblings of one another and the panels siblings of one another. Rendering them as
/// this component does is what keeps that true; wrapping either in a div of your own breaks the
/// numbering and the panels land in the wrong place.
/// </para>
/// </remarks>
public sealed partial class UiMegamenu : Component
{
    public UiSize? Size { get; set; }

    /// <summary>Stretches a panel to the full width of the viewport.</summary>
    public bool? Full { get; set; }

    /// <summary>Widens the panels without going edge to edge.</summary>
    public bool? Wide { get; set; }

    /// <summary>Stacks the triggers instead of laying them in a row, for a sidebar.</summary>
    public bool? Vertical { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Nav.Class(UiClass.Compose(
            "megamenu",
            Size is { } size ? UiClassNames.MegamenuSize(size) : "",
            Full == true ? "megamenu-full" : "",
            Wide == true ? "megamenu-wide" : "",
            Vertical == true ? "megamenu-vertical" : "",
            Class))[
            Children ?? [],
            // The moving highlight, and it goes LAST on purpose: daisyUI selects the panels as
            // `[popover]:nth-of-type(n)`, which counts divs, so a div placed among them would shift
            // every panel's number and send the highlight to the wrong trigger.
            Div.Class("megamenu-active")
        ];
}
