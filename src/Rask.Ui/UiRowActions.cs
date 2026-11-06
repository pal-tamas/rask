using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     What a list row can do, in its last cell, behind one ⋯: look at it, edit it, what the page adds, and delete it.
///     It IS that cell — a <see cref="UiTableCell" /> at the end, with no vertical padding, held at the table's right edge.
/// </summary>
/// <remarks>
///     <code>
///     Ui.TableRow.Key(supplier.Id)[
///         Ui.TableCell.Cut(supplier.Name),
///         Ui.RowActions.Key(supplier.Id).View(Routes.SupplierPage(supplier.Id)).Edit(Routes.EditSupplierPage(supplier.Id))
///             .Remove(() => Delete(supplier), "Delete supplier", $"Delete {supplier.Name}?")
///     ]
///     </code>
///     <para>
///     Not a Flux component: an addition the owner approved (2026-11-04), drawn as Flux's table demo draws a row's menu —
///     a ghost, small ⋯ opening a <see cref="UiDropdown" /> at the bottom end — holding, in this order, <see cref="View" />,
///     <see cref="Edit" /> (or <see cref="OnEdit" />), what the page puts in it, and the delete last, apart and in red. The
///     delete asks first, in the layout's <see cref="UiConfirm" >Ui.Confirm</see>, and runs the page's delete only on yes.
///     The cell keeps to the table's right edge while the other columns scroll under it (Flux's sticky cell, on the page's
///     surface); its column is <c>Ui.TableColumn.RowMenu</c>. A row nothing can be done with has no ⋯.
///     </para>
///     <para>
///     The words are the <c>RaskStrings</c> catalog's — <c>RowActionsView</c>, <c>RowActionsEdit</c>,
///     <c>RowActionsRemove</c>, <c>RowActionsHeading</c>, <c>RemoveQuestion</c> — so a Hungarian reader reads Megtekintés,
///     Módosítás and Törlés.
///     </para>
///     <para>
///     What it draws is keyed by it, so every row's menu and items are told apart from every other row's without the page
///     keying them. What the page puts in it is the page's own, keyed among everything the page writes: leave those items
///     unkeyed, or key them by the row.
///     </para>
/// </remarks>
public sealed partial class UiRowActions : Component
{
    /// <summary>Where the row is opened to be looked at: the menu's first item, an eye. Unset, there is none.</summary>
    public RouteUrl? View { get; set; }

    /// <summary>Where the menu's edit item opens the row to be edited: <c>Ui.RowActions.Edit(Routes.EditSupplierPage(id))</c>. Unset, there is none.</summary>
    public RouteUrl? Edit { get; set; }

    /// <summary>
    ///     Runs when the reader asks to edit the row where it stands, rather than on a page of its own — the menu's edit
    ///     item is then a button. It wins over <see cref="Edit" />.
    /// </summary>
    public Callback OnEdit { get; set; }

    /// <summary>Runs when the reader says yes to deleting the row. Unset, there is no delete.</summary>
    public Callback OnRemove { get; set; }

    /// <summary>The title over the delete's question, and the delete item's words. "Delete" when unset.</summary>
    public string? RemoveHeading { get; set; }

    /// <summary>The question the delete asks first. "Are you sure you want to delete it?" when unset.</summary>
    public string? RemoveQuestion { get; set; }

    /// <summary>Classes for the call site, added to the cell's own.</summary>
    public string? Class { get; set; }

    /// <summary>
    ///     A delete, last in the menu, which asks <paramref name="question" /> under <paramref name="heading" /> first and runs
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

    // Flux's table demo: `<flux:dropdown position="bottom" align="end" offset="-25">` round a ghost, small ⋯, in a `py-0`
    // cell; Flux's dropdown page puts the delete last, after a separator, in red. The cell is Flux's sticky one, on the
    // page's surface (Ui.Main's), at no weight so the page's own background wins.
    /// <inheritdoc />
    protected override Component? Render()
    {
        var items = Items();
        var cell = Ui.TableCell.End.Sticky().Class("py-0", UiTableBox.Surface, Class).Data("ui-row-actions", "");
        if (items.Count == 0)
        {
            return cell;
        }

        return cell[
            Ui.Dropdown.Key("menu").Bottom.End.Offset(-25)[
                Ui.Button.Key("more").Ghost.Sm.Icon(Ui.IconName.EllipsisHorizontal).Tooltip(RaskStrings.Get(RaskString.RowActionsHeading, "Actions")),
                Ui.Menu[items]
            ]
        ];
    }

    private List<Component> Items()
    {
        List<Component> items = [];
        if (View is { } view)
        {
            items.Add(Ui.MenuItem.Key("view").Icon(Ui.IconName.Eye).Href(view)[RaskStrings.Get(RaskString.RowActionsView, "View")]);
        }

        if (OnEdit.HasValue || Edit.HasValue)
        {
            var edit = Ui.MenuItem.Key("edit").Icon(Ui.IconName.Pencil);
            items.Add((OnEdit.HasValue ? edit.OnClick(OnEdit) : edit.Href(Edit))[RaskStrings.Get(RaskString.RowActionsEdit, "Edit")]);
        }

        items.AddRange((Children ?? []).OfType<Component>());

        if (OnRemove.HasValue)
        {
            var word = RemoveHeading ?? RaskStrings.Get(RaskString.RowActionsRemove, "Delete");
            if (items.Count > 0)
            {
                items.Add(Ui.MenuSeparator.Key("apart"));
            }

            items.Add(UiRemoval.Asking(Ui.MenuItem.Key("remove").Danger.Icon(Ui.IconName.Trash), OnRemove, RemoveHeading, RemoveQuestion, word)[word]);
        }

        return items;
    }
}
