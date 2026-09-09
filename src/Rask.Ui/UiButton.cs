using Rask.Core.Components;

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
/// <b>It IS a <c>&lt;button&gt;</c>.</b> It derives from <see cref="Button" /> rather than wrapping one,
/// so every button attribute is already here — <c>Type</c> (a form's commit button is
/// <c>.Type("submit")</c>), <c>Name</c>/<c>Value</c>, the <c>Form*</c> overrides, <c>PopoverTarget</c>,
/// <c>Command</c>/<c>CommandFor</c> — and so is the whole element surface: <c>Id</c>, <c>Style</c>,
/// <c>Data</c>, <c>Aria</c>, <c>Role</c>, <c>TabIndex</c> and every <c>On*</c> handler.
/// </para>
/// <para>
/// Wrapping was the reason this class kept growing a property at a time. It had none of the above, so a
/// call site needing one either could not use the kit or waited for the property to be added: an
/// <c>Id</c> for a test hook, then <c>Type</c> for a submit button, then <c>Command</c> for a scriptless
/// dialog, then <c>Aria</c>. Inheriting ends the queue.
/// </para>
/// <para>
/// <b>The trade is content.</b> A component renders through <c>Render()</c>; an element does not — the
/// serializer writes its tag and walks the children the caller supplied. So this cannot invent content,
/// and the <c>Label</c> and <c>Icon</c> properties are gone: a button's content is its children, exactly
/// as with a plain <c>&lt;button&gt;</c>.
/// </para>
/// <code>
/// UiButton.Tone(UiTone.Primary)[UiIcon.Name(UiIconName.EyeDropper).Class("me-1"), "Pick a color"]
/// </code>
/// <para>
/// One consequence worth stating: <see cref="Square" /> and <see cref="Circle" /> hold a single glyph and
/// no text, so a button drawn that way has no accessible name unless the call site gives it one with
/// <c>.Aria(…)</c>. A required <c>Label</c> used to guarantee that. It is the call site's to get right
/// now — which is already true of every plain <c>&lt;button&gt;</c> in the framework.
/// </para>
/// </remarks>
public sealed partial class UiButton : Button
{
    /// <summary>The button's colour. Omitted, it is the theme's plain button.</summary>
    public UiTone? Tone { get; set; }

    /// <summary>How it is filled. <see cref="UiVariant.Ghost" /> is the quiet action.</summary>
    public UiVariant? Variant { get; set; }

    public UiSize? Size { get; set; }

    /// <summary>Fills the width of its container, which is what a button in a phone-width form wants.</summary>
    public bool? Block { get; set; }

    /// <summary>Wider than its content needs, without filling the container as <see cref="Block" /> does.</summary>
    public bool? Wide { get; set; }

    /// <summary>Draws it as a square holding a single glyph.</summary>
    public bool? Square { get; set; }

    /// <summary>Draws it as a circle holding a single glyph, as <see cref="Square" />.</summary>
    public bool? Circle { get; set; }

    /// <summary>
    ///     Draws it as though it were being pressed. For a button that toggles something, where the
    ///     pressed look IS the state — a filter that is on, a panel that is showing.
    /// </summary>
    public bool? Active { get; set; }

    // The safe default, kept. Button's own remark is the reason: an unset type means SUBMIT inside a
    // form, "the usual cause of a page that reloads when you did not expect it". The wrapper used to
    // hard-code type="button" and so could never be a submit button at all; inheriting Type made submit
    // possible and silently took the default away with it, which would have turned every converted
    // button inside a <form> into one that submits it. Defaulted here instead, so .Type("submit") is
    // available and "button" is what you get by saying nothing.
    /// <inheritdoc />
    protected override void WriteAttributes(System.Text.StringBuilder sb)
    {
        Type ??= "button";
        base.WriteAttributes(sb);
    }

    // The class attribute, composed rather than replaced. This is the seam Element documents for exactly
    // this ("Subclasses transform the `class` attribute value without re-implementing the universal
    // id/class/style/data-* walk"), and the one NavLink already uses to splice in its active class.
    //
    // Declaring a `Class` property here instead would be the trap: it would shadow Element's, the
    // generator would emit a second non-generic setter that wins overload resolution, and the value would
    // land on a property that nothing renders.
    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
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
}
