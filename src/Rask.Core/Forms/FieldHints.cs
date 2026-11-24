namespace Rask.Core.Forms;

/// <summary>
///     The <c>[UIHint]</c> names a bound field knows — <c>[UIHint(FieldHints.Switch)] public bool Notify { get; set; }</c>
///     — each the kit control it draws where the member holds what that control edits. Any other hint is kept in
///     <see cref="FieldShape.Hint" /> for a control of the app's own, and the field draws its usual control.
/// </summary>
public static class FieldHints
{
    /// <summary>A text drawn as a textarea — also what <c>[DataType(DataType.MultilineText)]</c> asks for.</summary>
    public const string MultilineText = "MultilineText";

    /// <summary>A <c>bool</c> drawn as a switch rather than a checkbox.</summary>
    public const string Switch = "Switch";

    /// <summary>An enum drawn as a group of radio buttons rather than a select.</summary>
    public const string RadioGroup = "RadioGroup";
}
