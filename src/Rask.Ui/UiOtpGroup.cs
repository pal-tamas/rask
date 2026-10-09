namespace Rask;

/// <summary>
/// Flux UI's OTP group: the <see cref="UiOtpInput" /> cells inside it drawn as one joined box.
/// </summary>
public sealed partial class UiOtpGroup : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("flex " + UiInputGroup.Fuse).Data("ui-input-group", "")[Children ?? []];
}
