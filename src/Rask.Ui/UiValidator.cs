namespace Rask.Ui;

/// <summary>
/// The message a field shows when what was typed is not acceptable.
/// </summary>
/// <remarks>
///     <para>
///         Pair it with a field carrying <see cref="UiTone.Error" />. It keeps daisyUI's
///         <c>validator-hint</c> box, so the message occupies its space whether or not it is showing
///         and the form does not jump as the reader types.
///     </para>
///     <para>
///         It does NOT inherit daisyUI's hidden-until-invalid behaviour, and that is deliberate. That
///         is a CSS-only mechanism — <c>.validator:user-invalid ~ .validator-hint</c> — which asks the
///         BROWSER whether the value is acceptable and requires the hint to be a SIBLING of the input.
///         Neither holds here: this component is rendered by C#, only while the value is actually
///         wrong, so its presence is already the answer; and a field wraps its control in a
///         <c>&lt;label&gt;</c>, so a validator placed beside the field is not a sibling of anything
///         the selector can see. Left to daisyUI the message renders with the right text, in the right
///         place, and is invisible — which is the failure a reader can neither see nor report.
///     </para>
/// </remarks>
public sealed partial class UiValidator : Component
{
    public required string Message { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        P.Class(UiClass.Compose("validator-hint", UiClass.ValidatorShown, Class))[Message];
}
