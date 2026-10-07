namespace Rask;

/// <summary>
///     Flux's <c>flux:modal.close</c>: makes the button inside it close the <see cref="UiModal" /> it is in.
/// </summary>
/// <remarks>
///     A close, not a dismissal: <see cref="UiModal.OnClose" /> runs and <see cref="UiModal.OnCancel" /> does
///     not. In a modal the browser opened the button becomes a <c>command="close"</c> invoker; in one the page
///     owns, pressing it runs <see cref="UiModal.OnClose" /> after the button's own handler.
/// </remarks>
public sealed partial class UiModalClose : Component
{
    /// <inheritdoc />
    protected override Component? Render()
    {
        var close = Div.Class("inline").Attributes(("data-ui-modal-close", null));
        // Once: an enumerable that builds its components as it is read would hand the page unwired ones.
        Component?[] children = [.. Children ?? []];

        switch (Context.Get<UiModalScope>())
        {
            case { Name: { } name }:
                UiModalInvoker.Wire(children, UiModalInvoker.Closes(name));
                break;

            // Content that is not an element has no click of its own, so the wrapper hears it.
            case { Close: { HasValue: true } onClose } when !UiModalInvoker.Press(children, onClose):
                close = close.OnClick(onClose);
                break;
        }

        return close[children];
    }
}
