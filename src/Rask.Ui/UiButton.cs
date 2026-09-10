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
/// <see cref="OnClick" /> takes either shape — an action that awaits, or a state flip that does not —
/// because both call sites exist and neither should have to wrap a void in a completed task. One
/// property, two overloads on the step.
/// </para>
/// </remarks>
public sealed partial class UiButton : Component
{
    public required string Label { get; set; }

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
    public bool? Circle { get; set; }

    /// <summary>
    ///     Draws it as though it were being pressed. For a button that toggles something, where the
    ///     pressed look IS the state — a filter that is on, a panel that is showing.
    /// </summary>
    public bool? Active { get; set; }

    public Callback? OnClick { get; set; }

    /// <summary>
    ///     Where it goes. Set this and it renders an <c>&lt;a&gt;</c> rather than a <c>&lt;button&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A link that looks like a button is an ordinary thing to want — a call to action, a "read the
    ///     guide", an install link — and daisyUI documents <c>btn</c> on an anchor for exactly it. Without
    ///     this the only way to draw one was a class string in the application, which is the parallel
    ///     vocabulary the kit exists to remove.
    ///     </para>
    ///     <para>
    ///     It stays ONE component rather than a second one because the three axes — tone, fill, size — and
    ///     the icon and block treatments are identical either way; a sibling would duplicate all of them to
    ///     change one tag. What does change is what the element means: an anchor navigates, so it takes no
    ///     <c>type</c>, and <see cref="Disabled" /> cannot apply to it — there is no disabled state for a
    ///     link in HTML, and faking one with a class leaves it focusable and followable by keyboard. A
    ///     disabled link is a link that should not be rendered.
    ///     </para>
    ///     <para>
    ///     <see cref="OnClick" /> still works alongside it, for the case where a navigation also records
    ///     something — but if you find yourself reaching for both to avoid navigating at all, you want a
    ///     button.
    ///     </para>
    /// </remarks>
    public string? Href { get; set; }

    /// <summary>Opens <see cref="Href" /> in a new tab, with the <c>rel</c> that makes that safe.</summary>
    /// <remarks>
    ///     <c>rel="noopener"</c> comes with it rather than being left to the caller: a new tab opened
    ///     without it can reach back through <c>window.opener</c>, and the one thing a caller will forget
    ///     is the attribute that has no visible effect. Ignored when <see cref="Href" /> is not set.
    /// </remarks>
    public bool? NewTab { get; set; }

    /// <summary>
    ///     Whether it is disabled. Only meaningful for a button — see the remarks on <see cref="Href" />.
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>
    ///     The element's <c>id</c>.
    /// </summary>
    /// <remarks>
    ///     Present because a control has to be addressable: a <c>&lt;label for&gt;</c>, an
    ///     <c>aria-describedby</c>, a deep link, and the browser suite's own selectors all reach it by id.
    ///     The kit used to expose none outside <c>UiModal</c>, which meant an application that needed one had
    ///     to drop back to a raw element and a class string — the parallel vocabulary the kit exists to
    ///     remove.
    /// </remarks>
    public string? Id { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        // A square or a circle is sized to hold one glyph, so visible text would overflow it. The label
        // is still REQUIRED — it becomes the accessible name, because a button whose only content is a
        // decorative icon has no name at all, and a screen reader announces it as "button".
        var iconOnly = Square == true || Circle == true;

        // The classes are the same either way — that is the point of one component — so they are composed
        // once and the tag is chosen after.
        var classes = UiClass.Compose(
                "btn",
                Tone is { } tone ? UiClassNames.ButtonTone(tone) : "",
                Variant is { } variant ? UiClassNames.ButtonVariant(variant) : "",
                Size is { } size ? UiClassNames.ButtonSize(size) : "",
                Block == true ? "btn-block" : "",
                Wide == true ? "btn-wide" : "",
                Square == true ? "btn-square" : "",
                Circle == true ? "btn-circle" : "",
                Active == true ? "btn-active" : "",
                Class);

        // The two children, built once and handed to whichever tag wins below. Passed as two arguments
        // rather than wrapped: the indexer takes them directly, and `Fragment` is RaskMarkup's, which a
        // Component cannot reach.
        var glyph = Icon is { } icon ? UiIcon.Name(icon).Class("size-4 shrink-0") : null;
        var text = iconOnly ? null : Span[Label];

        // A square or a circle holds one glyph, so the label has to reach a screen reader some other way.
        var aria = new Dictionary<string, string?> { ["label"] = Label };

        if (Href is { } href)
        {
            var link = A.Href(href).Id(Id).Class(classes);

            if (iconOnly)
            {
                link = link.Aria(aria);
            }

            if (NewTab == true)
            {
                // noopener with it, always — see the remarks on NewTab.
                link = link.Target("_blank").Rel("noopener");
            }

            if (OnClick is { } navigateClick)
            {
                link = link.OnClick(navigateClick);
            }

            // No `type`, and no `disabled`: neither means anything on an anchor, and a disabled-looking
            // link is still focusable and still followable.
            return link[glyph, text];
        }

        var button = Button.Type("button").Id(Id).Class(classes).Disabled(Disabled == true);

        if (iconOnly)
        {
            button = button.Aria(aria);
        }

        // Forwarded as the carrier it arrived in, so the shape the caller wrote — sync or async — is the
        // shape the DOM slot holds. There is no "both set" to arbitrate any more: one property, one slot.
        if (OnClick is { } click)
        {
            button = button.OnClick(click);
        }

        return button[glyph, text];
    }
}
