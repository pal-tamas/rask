namespace Rask;

/// <summary>
/// A hint shown on hover or focus.
/// </summary>
/// <remarks>
/// CSS-only, from daisyUI's <c>data-tip</c>. It is a hint and nothing more: a tooltip is easy to miss, so
/// nothing that matters should live only here — and what must reach a phone takes <see cref="Toggleable" />.
/// A tip on a disabled button still shows on hover: daisyUI's <c>.btn:disabled</c> takes no pointer events,
/// so the hover lands on this wrapper. A keyboard cannot reach a disabled button at all, so a tip that
/// explains why it is disabled needs to be said somewhere else too.
/// </remarks>
public sealed partial class UiTooltip : Component
{
    public required string Tip { get; set; }

    /// <summary>Which side of the thing it points at.</summary>
    public Ui.Position? Position { get; set; }

    /// <summary>Where along that side it sits.</summary>
    public Ui.Align? Align { get; set; }

    /// <summary>Anything but <see cref="Ui.Tone.Neutral" />, which daisyUI does not define for a tooltip.</summary>
    public Ui.Tone? Tone { get; set; }

    /// <summary>
    ///     Shows it without waiting for a hover. For walking someone through a screen — and the only way
    ///     a touch user ever sees one, since there is no hover on a touch screen.
    /// </summary>
    public bool? Open { get; set; }

    /// <summary>
    ///     A keyboard shortcut shown beside the tip — <c>"⌘S"</c> — the way Flux UI teaches an app's shortcuts
    ///     where the reader is already looking.
    /// </summary>
    /// <remarks>
    ///     With one, the tip is rendered as daisyUI's <c>tooltip-content</c> element rather than its
    ///     <c>data-tip</c> attribute, because an attribute can only hold text and the shortcut is a
    ///     <c>&lt;kbd&gt;</c>.
    /// </remarks>
    public string? Kbd { get; set; }

    /// <summary>
    ///     Shows it on a tap as well as on a hover, for a tip whose content matters on a phone. On a touch
    ///     screen there is no hover, so an ordinary tooltip is never seen there at all.
    /// </summary>
    /// <remarks>
    ///     The wrapper becomes focusable and the tip shows while anything in it has focus — a tap focuses it,
    ///     a tap elsewhere takes the focus and the tip away. No script, and it also keeps a keyboard user's tip
    ///     up while they read it.
    /// </remarks>
    public bool? Toggleable { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var wrapper = Div
            .Class(UiClass.Compose(
                "tooltip",
                Position is { } position ? UiClassNames.TooltipPosition(position) : "",
                Align is { } align ? UiClassNames.TooltipAlign(align) : "",
                Tone is { } tone ? UiClassNames.TooltipTone(tone) : "",
                Open == true ? "tooltip-open" : "",
                Toggleable == true ? "ui-tooltip-toggleable" : "",
                Class));

        if (Toggleable == true)
        {
            wrapper = wrapper.TabIndex(0);
        }

        if (Kbd is not { } kbd)
        {
            return wrapper.Attributes(("data-tip", Tip))[Children ?? []];
        }

        return wrapper[
            Div.Class("tooltip-content").Role("tooltip")[
                Tip,
                RaskMarkup.Kbd.Class("kbd kbd-xs ms-2 text-base-content")[kbd]
            ],
            Children
        ];
    }
}
