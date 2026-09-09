namespace Rask.Ui;

/// <summary>
/// A card that tilts towards the pointer.
/// </summary>
/// <remarks>
/// Pointer-only by construction — there is no hover on a touch screen and none from a keyboard — so
/// nothing may depend on the tilt. It is an effect on content that is already complete without it.
/// </remarks>
public sealed partial class UiHover3d : Div
{


    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose("hover-3d", Class);
}
