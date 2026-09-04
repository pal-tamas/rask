namespace Rask.Ui;

/// <summary>
/// A button that opens a panel beneath it.
/// </summary>
/// <remarks>
/// <para>
/// Built on <c>&lt;details&gt;</c>/<c>&lt;summary&gt;</c>, so opening and closing is the browser's, not a
/// script's: it works before any runtime has booted, on a prerendered page, and with JavaScript off. The
/// element also gives the keyboard behaviour and the expanded state to assistive technology for free,
/// which a div-and-click-handler has to reimplement and usually does not.
/// </para>
/// <para>
/// It does not close on outside click, because that genuinely needs script. Where that matters, prefer a
/// <see cref="UiModal" />, which the browser dismisses natively.
/// </para>
/// </remarks>
public sealed partial class UiDropdown : Component
{
    /// <summary>The label on the button that opens it.</summary>
    public required string Trigger { get; set; }

    /// <summary>Alignment, as one of daisyUI's placement classes (for example <c>dropdown-end</c>).</summary>
    public string? Placement { get; set; }

    public UiIconName? Icon { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Details.Class(UiClass.Compose("dropdown", Placement, Class))[
            Summary.Class("btn")[
                Icon is { } icon ? UiIcon.Name(icon).Class("size-4 shrink-0") : null,
                Span[Trigger]
            ],
            Ul.Class("dropdown-content menu z-1 w-52 rounded-box bg-base-100 p-2 shadow-sm")[
                Children ?? []
            ]
        ];
}

/// <summary>
/// Two pieces of content, one shown at a time, flipped by a checkbox.
/// </summary>
/// <remarks>
/// The state lives in a hidden checkbox and the swap happens in CSS, so this needs no script and keeps
/// working on a page whose runtime has not booted. Typical use is an icon that changes when something is
/// on — a menu button becoming a close button, a sound icon becoming a muted one.
/// </remarks>
public sealed partial class UiSwap : Component
{
    /// <summary>
    ///     The accessible name; both faces are decorative once it is set. Not <c>Label</c>, which MaryUI
    ///     uses: this renders a &lt;label&gt; element and a property of that name shadows its chain entry.
    /// </summary>
    public required string AccessibleLabel { get; set; }

    public required Component On { get; set; }

    public required Component Off { get; set; }

    /// <summary>An animation, as one of daisyUI's classes (<c>swap-rotate</c>, <c>swap-flip</c>).</summary>
    public string? Animation { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Label.Class(UiClass.Compose("swap", Animation, Class))[
            Input
                .Value(false)
                .Aria(new Dictionary<string, string?> { ["label"] = AccessibleLabel }),
            Div.Class("swap-on")[On],
            Div.Class("swap-off")[Off]
        ];
}

/// <summary>
/// A control that switches the theme, with no JavaScript.
/// </summary>
/// <remarks>
/// <para>
/// daisyUI matches <c>input.theme-controller[value=x]:checked</c> in CSS, so a checked input rewrites the
/// palette on its own. Nothing here reads or writes a preference: there is no script, so there is nothing
/// to persist it with, and the theme resets on navigation. That is the honest trade for zero JavaScript,
/// and it is why the default with no control at all follows the operating system.
/// </para>
/// <para>
/// It only has an effect inside the kit's theme scope — see
/// <see cref="UiStylesheet.ThemeScopeAttribute" />.
/// </para>
/// </remarks>
public sealed partial class UiThemeController : Component
{
    /// <summary>
    ///     daisyUI and MaryUI both call this <c>label</c>. Free to use here because this component renders no
    ///     &lt;label&gt; element of its own — where one does, the property is AccessibleLabel instead.
    /// </summary>
    /// <remarks>For example "Dark theme".</remarks>
    public required string Label { get; set; }

    /// <summary>The theme this control selects when checked. Defaults to <c>dark</c>.</summary>
    public string? Theme { get; set; }

    public UiSize? Size { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Input
            .Value(false)
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Attributes(("value", Theme ?? "dark"))
            .Class(UiClass.Compose(
                "toggle theme-controller",
                Size is { } size ? UiClassNames.ToggleSize(size) : "",
                Class));
}

/// <summary>
/// A titled section that opens and closes.
/// </summary>
/// <remarks>
/// <c>&lt;details&gt;</c> again, for the same reasons as <see cref="UiDropdown" />. Give several the same
/// <see cref="Group" /> and the browser closes the others when one opens — an accordion with no state to
/// manage and no script to manage it.
/// </remarks>
public sealed partial class UiCollapse : Component
{
    /// <summary>daisyUI and MaryUI both call this <c>title</c>. <c>new</c> because the base type carries a
    /// markup entry of that name; this component renders no &lt;title&gt; element, so nothing is lost.</summary>
    public new required string Title { get; set; }

    /// <summary>
    ///     The accordion this belongs to. Sections sharing a name are mutually exclusive; omit it and each
    ///     one opens and closes on its own.
    /// </summary>
    public string? Group { get; set; }

    public bool? Open { get; set; }

    /// <summary>Draws the arrow or plus marker, as daisyUI's <c>collapse-arrow</c>/<c>collapse-plus</c>.</summary>
    public string? Marker { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var details = Details
            .Class(UiClass.Compose("collapse border border-base-300 bg-base-100", Marker, Class))
            .Open(Open == true);

        if (Group is { } group)
        {
            // `name` on <details> is the newer exclusive-accordion attribute and Rask.Html does not model
            // it yet, so it goes through the verbatim escape hatch. It is what makes sections with the
            // same name close each other — the accordion behaviour, from the browser, with no state.
            details = details.Attributes(("name", group));
        }

        return details[
            Summary.Class("collapse-title font-semibold")[Title],
            Div.Class("collapse-content text-sm")[Children ?? []]
        ];
    }
}
