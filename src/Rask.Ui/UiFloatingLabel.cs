namespace Rask.Ui;

/// <summary>
/// A caption that sits in the field until it has content, then rises above it.
/// </summary>
/// <remarks>
/// <para>
/// Worth preferring over a placeholder-as-label, which is the pattern this replaces: a placeholder
/// disappears the moment typing starts, so the one thing saying what the field is for vanishes exactly
/// when a reader might check it, and it is invisible to anybody reviewing a filled-in form.
/// </para>
/// <para>
/// The text must come FIRST inside the wrapper — daisyUI selects the control as the sibling after it.
/// </para>
/// </remarks>
public sealed partial class UiFloatingLabel : Component
{
    /// <summary>What the field is for.</summary>
    public new required string Text { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Label.Class(UiClass.Compose("floating-label", Class))[
            Span[Text],
            Children ?? []
        ];
}
