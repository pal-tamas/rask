namespace Rask.Ui;

/// <summary>
/// The message a field shows when what was typed is not acceptable.
/// </summary>
/// <remarks>
/// Pair it with a field carrying <see cref="UiTone.Error" />. daisyUI's <c>validator-hint</c> is hidden
/// until the input beside it is invalid, so the message occupies its space whether or not it is showing
/// and the form does not jump as the reader types.
/// </remarks>
public sealed partial class UiValidator : Component
{
    public required string Message { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        P.Class(UiClass.Compose("validator-hint", Class))[Message];
}
