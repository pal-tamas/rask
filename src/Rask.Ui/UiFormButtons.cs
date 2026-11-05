using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     A form's button row, as Flux draws a form's footer: Delete at the start where the record can be deleted, then, at
///     the end, Cancel back to the list and Save.
/// </summary>
/// <remarks>
///     <code>
///     Ui.FormButtons.Back(Routes.SuppliersPage())                                   // Cancel · Save
///     Ui.FormButtons.Back(Routes.SuppliersPage()).Remove(Delete, "Delete supplier", "Delete Aminoff Freight?")
///     </code>
///     <para>
///     Not a Flux component: an addition the owner approved (2026-11-04), drawn from Flux's own form footer — a row of
///     <c>gap-2</c>, a <see cref="UiSpacer" />, a ghost Cancel and the primary submit. Cancel is a link to the list, so a
///     form's <c>.ConfirmLeave()</c> asks before it drops what was typed; Save submits the form it stands in. Delete asks
///     first, in the layout's <see cref="UiConfirm" >Ui.Confirm</see>, and runs the page's delete only on yes. What the
///     page puts in it (a second action) stands before Cancel.
///     </para>
///     <para>
///     The words are the <c>RaskStrings</c> catalog's — <c>FormButtonsCancel</c>, <c>FormButtonsSave</c>,
///     <c>FormButtonsRemove</c>, <c>RemoveQuestion</c> — so a Hungarian reader reads Mégse, Mentés and Törlés.
///     </para>
/// </remarks>
public sealed partial class UiFormButtons : Component
{
    /// <summary>The list Cancel goes back to. Required, and taken first: <c>Ui.FormButtons.Back(Routes.SuppliersPage())</c>.</summary>
    public RouteUrl Back { get; set; }

    /// <summary>Runs when the reader says yes to deleting the record. Unset, there is no Delete button.</summary>
    public Callback OnRemove { get; set; }

    /// <summary>The title over the delete's question. "Delete" when unset.</summary>
    public string? RemoveHeading { get; set; }

    /// <summary>The question the delete asks first. "Are you sure you want to delete it?" when unset.</summary>
    public string? RemoveQuestion { get; set; }

    /// <summary>The label of the button back to the list: <c>.Cancel("Back")</c>. The catalog's "Cancel" (Mégse) when unset.</summary>
    public string? Cancel { get; set; }

    /// <summary>The label of the submit: <c>.Save("Create")</c> on a new record. The catalog's "Save" (Mentés) when unset.</summary>
    public string? Save { get; set; }

    /// <summary>Classes for the call site, added to the row's own.</summary>
    public string? Class { get; set; }

    /// <summary>
    ///     A Delete button at the row's start, which asks <paramref name="question" /> under <paramref name="heading" />
    ///     first and runs <paramref name="onRemove" /> on yes. A null <paramref name="onRemove" /> draws none: a record that
    ///     cannot be deleted.
    /// </summary>
    /// <param name="onRemove">The page's delete.</param>
    /// <param name="heading">The title over the question; "Delete" when null.</param>
    /// <param name="question">The question; "Are you sure you want to delete it?" when null.</param>
    /// <returns>These buttons.</returns>
    public UiFormButtons Remove(Func<Task>? onRemove, string? heading = null, string? question = null) =>
        onRemove is null ? this : this.OnRemove(onRemove).RemoveHeading(heading).RemoveQuestion(question);

    /// <inheritdoc cref="Remove(Func{Task}, string, string)" />
    public UiFormButtons Remove(Action? onRemove, string? heading = null, string? question = null) =>
        onRemove is null ? this : this.OnRemove(onRemove).RemoveHeading(heading).RemoveQuestion(question);

    /// <inheritdoc />
    protected override Component? Render()
    {
        var remove = RaskStrings.Get(RaskString.FormButtonsRemove, "Delete");

        return Div.Class("[:where(&)]:flex [:where(&)]:gap-2", Class).Data("ui-form-buttons", "")[
            OnRemove.HasValue ? UiRemoval.Asking(Ui.Button.Key("remove").Danger, OnRemove, RemoveHeading, RemoveQuestion, remove)[remove] : null,
            Ui.Spacer,
            Children is { } more ? Fragment[more] : null,
            Ui.Button.Key("cancel").Ghost.Href(Back)[Cancel ?? RaskStrings.Get(RaskString.FormButtonsCancel, "Cancel")],
            Ui.Button.Key("save").Primary.Submit[Save ?? RaskStrings.Get(RaskString.FormButtonsSave, "Save")]
        ];
    }
}
