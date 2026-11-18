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
///     Form.Model(m).OnSubmit(Merge)[…, Ui.DialogButtons.Submit().Save("Merge")]   // the form's own submit
///     Ui.DialogButtons.Danger().OnSave(Delete).Save("Delete project")              // Flux's danger action
///     </code>
///     <para>
///     Not a Flux component: an addition the owner approved (2026-11-17, #1352), drawn from Flux's own modal footer —
///     a row of <c>gap-2</c>, a <see cref="UiSpacer" />, a ghost Cancel inside <see cref="UiModalClose" /> and the
///     primary action. Cancel closes the dialog the way Flux's <c>flux:modal.close</c> does, by the browser's own
///     <c>command="close"</c>: <see cref="UiModal.OnClose" /> runs, with no round trip. The action is a click handler,
///     not a submit, so a form the dialog stands in is not sent; it shows the button's busy state while
///     <see cref="OnSave" /> runs, as a form's Save does while it is sent. What the page puts in it (a second action)
///     stands before Cancel. A dialog that IS a form takes <see cref="Submit" /> instead: the action is then the form's
///     submit, as <see cref="UiFormButtons" />' Save is. A form's footer, Cancel back to a list and a submit, is <see cref="UiFormButtons" />.
///     </para>
///     <para>
///     The words are the <c>RaskStrings</c> catalog's — <c>FormButtonsCancel</c>, <c>FormButtonsSave</c> — so a
///     Hungarian reader reads Mégse and Mentés.
///     </para>
/// </remarks>
public sealed partial class UiDialogButtons : Component
{
    private static readonly UiPartMarker Marker = new("ui-dialog-buttons");

    private bool _submits;
    private bool _danger;

    /// <summary>Runs when the action is pressed: <c>.OnSave(Book)</c>. Unset, the action does nothing.</summary>
    public Callback OnSave { get; set; }

    /// <summary>The label of the button that closes the dialog: <c>.Cancel("Back")</c>. The catalog's "Cancel" (Mégse) when unset.</summary>
    public string? Cancel { get; set; }

    /// <summary>The label of the action: <c>.Save("Book")</c>. The catalog's "Save" (Mentés) when unset.</summary>
    public string? Save { get; set; }

    /// <summary>
    ///     Makes the action the submit of the form the dialog stands in, as <see cref="UiFormButtons" />' Save is:
    ///     <c>Form.Model(m).OnSubmit(Merge)[…, Ui.DialogButtons.Submit().Save("Merge")]</c>.
    /// </summary>
    /// <remarks>
    ///     The action is a <c>type="submit"</c> button, so the form does the rest: its validation runs, its
    ///     <c>OnSubmit</c> runs only when every field passes, the button shows its busy state while the submit is sent,
    ///     and Enter in a field submits. Cancel still closes the dialog without sending it. The form's handler is the
    ///     action, so <see cref="OnSave" /> is not given beside it: the two together throw when the row is drawn.
    /// </remarks>
    /// <returns>These buttons.</returns>
    public UiDialogButtons Submit()
    {
        _submits = true;
        // What a generated step does when it writes a new value: the row is drawn again with it.
        BuilderRuntime.MarkChanged(this);
        return this;
    }

    /// <summary>
    ///     Draws the action as Flux's danger button, as Flux's "Delete project?" modal does:
    ///     <c>Ui.DialogButtons.Danger().OnSave(Delete).Save("Delete project")</c>.
    /// </summary>
    /// <remarks>Only how it is drawn: it is a click with <see cref="OnSave" /> and the form's submit with <see cref="Submit" />.</remarks>
    /// <returns>These buttons.</returns>
    public UiDialogButtons Danger()
    {
        _danger = true;
        BuilderRuntime.MarkChanged(this);
        return this;
    }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("[:where(&)]:flex [:where(&)]:gap-2").Data(Marker.With(null))[
            Ui.Spacer,
            Children is { } more ? Fragment[more] : null,
            Ui.ModalClose[Ui.Button.Key("cancel").Ghost[Cancel ?? RaskStrings.Get(RaskString.FormButtonsCancel, "Cancel")]],
            Action()[Save ?? RaskStrings.Get(RaskString.FormButtonsSave, "Save")]
        ];

    private UiButton Action()
    {
        var action = Ui.Button.Key("save").Variant(_danger ? Ui.ButtonVariant.Danger : Ui.ButtonVariant.Primary);

        if (!_submits)
        {
            return action.OnClick(OnSave);
        }

        if (OnSave.HasValue)
        {
            throw new InvalidOperationException(
                "Ui.DialogButtons.Submit() and .OnSave(…) both name the action. With .Submit() the form's own OnSubmit "
                + "is the action: drop .OnSave(…) — Form.Model(m).OnSubmit(Save)[…, Ui.DialogButtons.Submit()] — or drop "
                + ".Submit() to keep the click.");
        }

        return action.Type(Ui.ButtonType.Submit);
    }
}
