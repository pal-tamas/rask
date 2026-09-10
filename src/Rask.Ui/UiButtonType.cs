namespace Rask.Ui;

/// <summary>
/// What a <see cref="UiButton" /> does when it is pressed, as the <c>type</c> attribute.
/// </summary>
/// <remarks>
/// <para>
/// Typed rather than a string because the wrong value here fails SILENTLY and functionally. A button
/// inside a form defaults to <c>type="submit"</c> in HTML, which is why Rask's own buttons set
/// <c>type="button"</c> explicitly — a button that only meant to toggle something otherwise submits the
/// form around it. The mirror of that mistake is just as quiet: a form's real submit button rendered as
/// <c>type="button"</c> does nothing at all when pressed, and the form looks complete.
/// </para>
/// <para>
/// <see cref="UiButton" /> had no way to say it, and the showcase's submit buttons reached past the kit to
/// a raw <c>Button.Type("submit")</c> — twenty-one of them, plus one reset.
/// </para>
/// </remarks>
public enum UiButtonType
{
    /// <summary>Does nothing on its own. The default, and what a button with an <c>OnClick</c> wants.</summary>
    Button = 0,

    /// <summary>Submits the form it is in.</summary>
    Submit,

    /// <summary>Returns the form it is in to its initial values.</summary>
    Reset,
}
