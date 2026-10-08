namespace Rask;

/// <summary>
/// Flux UI's OTP separator: the dash between two cells, or two groups, of a <see cref="UiOtp" />.
/// </summary>
public sealed partial class UiOtpSeparator : Component
{
    /// <inheritdoc />
    protected override Component? Render() => Ui.Text.Class("px-2")["—"];
}
