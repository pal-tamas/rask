namespace Rask;

/// <summary>
///     A dialog's button row, as Flux draws a modal's footer: at the end, Cancel, which closes the
///     <see cref="UiModal" /> it stands in, and the action.
/// </summary>
/// <remarks>
///     <code>
///     Ui.Modal.Name("stock")[
///         …,
///         Ui.DialogButtons.OnSave(Book)                       // Cancel · Save
///     ]
///     Ui.DialogButtons.OnSave(Book).Save("Book").Cancel("Back")
///     </code>
///     <para>
///     Not a Flux component: an addition the owner approved (2026-11-17, #1352), drawn from Flux's own modal footer —
///     a row of <c>gap-2</c>, a <see cref="UiSpacer" />, a ghost Cancel inside <see cref="UiModalClose" /> and the
///     primary action. Cancel closes the dialog the way Flux's <c>flux:modal.close</c> does, by the browser's own
///     <c>command="close"</c>: <see cref="UiModal.OnClose" /> runs, with no round trip. The action is a click handler,
///     not a submit, so a form the dialog stands in is not sent; it shows the button's busy state while
///     <see cref="OnSave" /> runs, as a form's Save does while it is sent. What the page puts in it (a second action)
///     stands before Cancel. A form's footer, Cancel back to a list and a submit, is <see cref="UiFormButtons" />.
///     </para>
///     <para>
///     The words are the <c>RaskStrings</c> catalog's — <c>FormButtonsCancel</c>, <c>FormButtonsSave</c> — so a
///     Hungarian reader reads Mégse and Mentés.
///     </para>
/// </remarks>
public sealed partial class UiDialogButtons : Component
{
    private static readonly UiPartMarker Marker = new("ui-dialog-buttons");

    /// <summary>Runs when the action is pressed: <c>.OnSave(Book)</c>. Unset, the action does nothing.</summary>
    public Callback OnSave { get; set; }

    /// <summary>The label of the button that closes the dialog: <c>.Cancel("Back")</c>. The catalog's "Cancel" (Mégse) when unset.</summary>
    public string? Cancel { get; set; }

    /// <summary>The label of the action: <c>.Save("Book")</c>. The catalog's "Save" (Mentés) when unset.</summary>
    public string? Save { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("[:where(&)]:flex [:where(&)]:gap-2").Data(Marker.With(null))[
            Ui.Spacer,
            Children is { } more ? Fragment[more] : null,
            Ui.ModalClose[Ui.Button.Key("cancel").Ghost[Cancel ?? RaskStrings.Get(RaskString.FormButtonsCancel, "Cancel")]],
            Ui.Button.Key("save").Primary.OnClick(OnSave)[Save ?? RaskStrings.Get(RaskString.FormButtonsSave, "Save")]
        ];
}
