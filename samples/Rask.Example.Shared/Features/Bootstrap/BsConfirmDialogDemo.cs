namespace Rask.Example.Shared.Features;

// BsConfirmDialog — an "are you sure?" prompt layered on BsModal for destructive actions. Controlled
// like a modal: _open holds visibility, OnConfirm/OnCancel run your action and close it. The confirm
// button defaults to Danger. Zero-JS; dismissible via Cancel, the ×, or a backdrop click.
public sealed class BsConfirmDialogDemo : Component
{
    private bool _open;
    private string _status = "No action taken yet.";

    protected override Component? Render() =>
    [
        Div(Class: "vstack gap-2 align-items-start")[
            BsButton(Color: BsColor.Danger, OnClick: () => _open = true)["Delete item"],
            P(Id: "bs-confirm-status", Class: "mb-0 text-body-secondary")[_status]
        ],
        BsConfirmDialog(
            Open: _open,
            Title: "Delete item?",
            Message: "This can't be undone. Are you sure you want to delete it?",
            ConfirmText: "Delete",
            CancelText: "Keep it",
            OnConfirm: () => { _status = "Item deleted."; _open = false; },
            OnCancel: () => { _status = "Cancelled — nothing was deleted."; _open = false; })
    ];
}
