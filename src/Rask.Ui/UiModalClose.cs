namespace Rask;

/// <summary>
///     Flux's <c>flux:modal.close</c>: makes the button inside it close the <see cref="UiModal" /> it is in.
/// </summary>
/// <remarks>
///     A close, not a dismissal: <see cref="UiModal.OnClose" /> runs and <see cref="UiModal.OnCancel" /> does
///     not. The button becomes a <c>command="close"</c> invoker, so the browser closes the dialog and a
///     handler of the button's own still runs.
/// </remarks>
public sealed partial class UiModalClose : Component
{
    private static readonly UiPartMarker Marker = new("ui-modal-close");

    /// <inheritdoc />
    protected override Component? Render()
    {
        // Once: an enumerable that builds its components as it is read would hand the page unwired ones.
        Component?[] children = [.. Children ?? []];

        if (Context.Get<UiModalScope>() is { } modal)
        {
            UiModalInvoker.Wire(children, UiModalInvoker.Closes(modal.Id));
        }

        return Div.Class("inline").Data(Marker.With(null))[children];
    }
}
