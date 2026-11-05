using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     What a list row can do, at its end: open it to be edited (a pencil) and, where rows can be deleted, delete it (a
///     bin).
/// </summary>
/// <remarks>
///     <code>
///     Ui.TableCell.End.Class("py-0")[
///         Ui.RowActions.Edit(Routes.EditSupplierPage(supplier.Id)).Remove(() => Delete(supplier), "Delete supplier", $"Delete {supplier.Name}?")
///     ]
///     </code>
///     <para>
///     Not a Flux component: an addition the owner approved (2026-11-04), drawn as Flux draws a row's action — a ghost,
///     small, square button in a <c>py-0</c> cell — with the outline drawing in the muted ink, darkening under the
///     pointer. Each names itself in its tooltip. The bin asks first, in the layout's <see cref="UiConfirm" >Ui.Confirm</see>,
///     and runs the page's delete only on yes. What the page puts in it (an export, a download) stands before the pencil.
///     Its column is <c>Ui.TableColumn.Width(Ui.ColumnWidth.Actions)</c>.
///     </para>
///     <para>
///     The words are the <c>RaskStrings</c> catalog's — <c>RowActionsEdit</c>, <c>RowActionsRemove</c>,
///     <c>RemoveQuestion</c> — so a Hungarian reader reads Módosítás and Törlés.
///     </para>
/// </remarks>
public sealed partial class UiRowActions : Component
{
    // Flux's row action, with the outline drawing in the muted ink that darkens under the pointer.
    private const string Muted = "text-zinc-400 hover:text-zinc-800 dark:text-zinc-400 dark:hover:text-white";

    /// <summary>Where the pencil opens the row to be edited. Required, and taken first: <c>Ui.RowActions.Edit(Routes.EditSupplierPage(id))</c>.</summary>
    public RouteUrl Edit { get; set; }

    /// <summary>Runs when the reader says yes to deleting the row. Unset, there is no bin.</summary>
    public Callback OnRemove { get; set; }

    /// <summary>The title over the bin's question. "Delete" when unset.</summary>
    public string? RemoveHeading { get; set; }

    /// <summary>The question the bin asks first. "Are you sure you want to delete it?" when unset.</summary>
    public string? RemoveQuestion { get; set; }

    /// <summary>Classes for the call site, added to the row's own.</summary>
    public string? Class { get; set; }

    /// <summary>
    ///     A bin beside the pencil, which asks <paramref name="question" /> under <paramref name="heading" /> first and runs
    ///     <paramref name="onRemove" /> on yes. A null <paramref name="onRemove" /> draws none: a row that cannot be deleted.
    /// </summary>
    /// <param name="onRemove">The page's delete of this row.</param>
    /// <param name="heading">The title over the question; "Delete" when null.</param>
    /// <param name="question">The question; "Are you sure you want to delete it?" when null.</param>
    /// <returns>These actions.</returns>
    public UiRowActions Remove(Func<Task>? onRemove, string? heading = null, string? question = null) =>
        onRemove is null ? this : this.OnRemove(onRemove).RemoveHeading(heading).RemoveQuestion(question);

    /// <inheritdoc cref="Remove(Func{Task}, string, string)" />
    public UiRowActions Remove(Action? onRemove, string? heading = null, string? question = null) =>
        onRemove is null ? this : this.OnRemove(onRemove).RemoveHeading(heading).RemoveQuestion(question);

    /// <inheritdoc />
    protected override Component? Render()
    {
        var remove = RaskStrings.Get(RaskString.RowActionsRemove, "Delete");

        return Div.Class("[:where(&)]:flex justify-end [:where(&)]:gap-1", Class).Data("ui-row-actions", "")[
            Children is { } more ? Fragment[more] : null,
            Pressed(Ui.IconName.Pencil, RaskStrings.Get(RaskString.RowActionsEdit, "Edit")).Key("edit").Href(Edit),
            OnRemove.HasValue ? UiRemoval.Asking(Pressed(Ui.IconName.Trash, remove).Key("remove"), OnRemove, RemoveHeading, RemoveQuestion, remove) : null
        ];
    }

    private static UiButton Pressed(Ui.IconName icon, string word) =>
        Ui.Button.Ghost.Sm.Icon(icon).IconVariant(Ui.IconVariant.Outline).Class(Muted).Tooltip(word);
}
