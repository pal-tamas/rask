namespace Rask.Ui;

/// <summary>
/// A button.
/// </summary>
/// <remarks>
/// <para>
/// daisyUI's <c>btn</c>, which carries the whole of what this component used to hand-roll: the touch
/// target, the focus ring, the disabled treatment, the hover transition and the four weights. Colour
/// (<see cref="Tone" />), fill (<see cref="Variant" />) and <see cref="Size" /> are three independent
/// axes and compose, so an outlined error button needs no member of its own.
/// </para>
/// <para>
/// Both <see cref="OnClick" /> and <see cref="OnClickAsync" /> exist because both call sites exist: an
/// action that awaits, and a state flip that does not. Making every caller wrap a void in a completed
/// task would be noise at the call site to save one property here.
/// </para>
/// </remarks>
public sealed partial class UiButton : Component
{
    public new required string Label { get; set; }

    /// <summary>The button's colour. Omitted, it is the theme's plain button.</summary>
    public UiTone? Tone { get; set; }

    /// <summary>How it is filled. <see cref="UiVariant.Ghost" /> is the quiet action.</summary>
    public UiVariant? Variant { get; set; }

    public UiSize? Size { get; set; }

    public UiIconName? Icon { get; set; }

    /// <summary>Fills the width of its container, which is what a button in a phone-width form wants.</summary>
    public bool? Block { get; set; }

    /// <summary>Wider than its content needs, without filling the container as <see cref="Block" /> does.</summary>
    public bool? Wide { get; set; }

    /// <summary>
    ///     Draws it as a square holding nothing but its <see cref="Icon" />. <see cref="Label" /> becomes
    ///     the accessible name rather than visible text — see the remarks on icon-only buttons.
    /// </summary>
    public bool? Square { get; set; }

    /// <summary>Draws it as a circle holding nothing but its <see cref="Icon" />, as <see cref="Square" />.</summary>
    public new bool? Circle { get; set; }

    /// <summary>
    ///     Draws it as though it were being pressed. For a button that toggles something, where the
    ///     pressed look IS the state — a filter that is on, a panel that is showing.
    /// </summary>
    public bool? Active { get; set; }

    public Action? OnClick { get; set; }

    public Func<Task>? OnClickAsync { get; set; }

    public bool? Disabled { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        // A square or a circle is sized to hold one glyph, so visible text would overflow it. The label
        // is still REQUIRED — it becomes the accessible name, because a button whose only content is a
        // decorative icon has no name at all, and a screen reader announces it as "button".
        var iconOnly = Square == true || Circle == true;

        var button = Button
            .Type("button")
            .Class(UiClass.Compose(
                "btn",
                Tone is { } tone ? UiClassNames.ButtonTone(tone) : "",
                Variant is { } variant ? UiClassNames.ButtonVariant(variant) : "",
                Size is { } size ? UiClassNames.ButtonSize(size) : "",
                Block == true ? "btn-block" : "",
                Wide == true ? "btn-wide" : "",
                Square == true ? "btn-square" : "",
                Circle == true ? "btn-circle" : "",
                Active == true ? "btn-active" : "",
                Class))
            .Disabled(Disabled == true);

        if (iconOnly)
        {
            button = button.Aria(new Dictionary<string, string?> { ["label"] = Label });
        }

        // Whichever the caller supplied. Both set would be a call-site bug, and the async one wins because
        // it is the one that does work.
        if (OnClickAsync is { } async)
        {
            button = button.OnClickAsync(async);
        }
        else if (OnClick is { } sync)
        {
            button = button.OnClick(sync);
        }

        return button[
            Icon is { } icon ? UiIcon.Name(icon).Class("size-4 shrink-0") : null,
            iconOnly ? null : Span[Label]
        ];
    }
}
