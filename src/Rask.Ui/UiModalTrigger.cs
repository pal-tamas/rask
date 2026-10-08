namespace Rask;

/// <summary>
///     Flux's <c>flux:modal.trigger</c>: makes the button inside it open the <see cref="UiModal" /> of that
///     <see cref="Name" />.
/// </summary>
/// <remarks>
///     No handler runs. The button becomes an invoker — <c>command="show-modal"</c> naming the dialog — so
///     the browser opens it (the runtime, where the engine has no invoker commands). It draws nothing of its own:
///     <c>Ui.ModalTrigger.Name("edit-profile")[Ui.Button["Edit profile"]]</c>.
/// </remarks>
public sealed partial class UiModalTrigger : Component
{
    private static readonly UiPartMarker Marker = new("ui-modal-trigger");

    /// <summary>The <see cref="UiModal.Name" /> of the modal to open.</summary>
    public required string Name { get; set; }

    /// <summary>
    ///     A key combination that opens it from anywhere on the page: <c>mod+k</c> is ⌘K on a Mac and Ctrl K
    ///     elsewhere, where Flux writes <c>cmd.k</c>.
    /// </summary>
    public string? Shortcut { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var opens = UiModalInvoker.Opens(Name);
        // Once: an enumerable that builds its components as it is read would hand the page unwired ones.
        Component?[] children = [.. Children ?? []];

        if (!UiModalInvoker.Wire(children, Shortcut is { } shortcut ? [.. opens, ("data-rask-shortcut", shortcut)] : opens))
        {
            throw new InvalidOperationException(
                $"Ui.ModalTrigger.Name(\"{Name}\") has no button to press. Give it one: "
                + $"Ui.ModalTrigger.Name(\"{Name}\")[Ui.Button[\"Open\"]].");
        }

        return Div.Class("contents").Data(Marker.With(null))[children];
    }
}
