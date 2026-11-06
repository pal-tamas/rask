using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     What a list row can do, in its last cell: open it to be edited (a pencil) and, where rows can be deleted, delete it
///     (a bin) — or, with <see cref="Menu" />, all of it behind one ⋯. It IS that cell — a <see cref="UiTableCell" /> at the
///     end, with no vertical padding.
/// </summary>
/// <remarks>
///     <code>
///     Ui.TableRow.Key(supplier.Id)[
///         Ui.TableCell.Cut(supplier.Name),
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
///     <c>Ui.RowActions.Menu()</c> draws the row's menu as Flux's table demo does — a ghost ⋯ opening a
///     <see cref="UiDropdown" /> at the bottom end — holding, in this order, <see cref="View" />, <see cref="Edit" /> (or
///     <see cref="OnEdit" />), what the page puts in it, and the delete last, apart and in red. The cell then keeps to the
///     table's right edge while the other columns scroll under it (Flux's sticky cell, on the page's surface); its column is
///     <c>Ui.TableColumn.Width(Ui.ColumnWidth.RowMenu)</c>. A row nothing can be done with has no ⋯.
///     </para>
///     <para>
///     The words are the <c>RaskStrings</c> catalog's — <c>RowActionsView</c>, <c>RowActionsEdit</c>,
///     <c>RowActionsRemove</c>, <c>RowActionsHeading</c>, <c>RemoveQuestion</c> — so a Hungarian reader reads Megtekintés,
///     Módosítás and Törlés.
///     </para>
///     <para>
///     What it draws is keyed by it, so every row's buttons, menu and items are told apart from every other row's without
///     the page keying them. What the page puts in it is the page's own, keyed among everything the page writes: leave
///     those items unkeyed, or key them by the row.
///     </para>
/// </remarks>
public sealed partial class UiRowActions : Component
{
    // Flux's row action, with the outline drawing in the muted ink that darkens under the pointer.
    private const string Muted = "text-zinc-400 hover:text-zinc-800 dark:text-zinc-400 dark:hover:text-white";

    /// <summary>
    ///     Draws everything behind one ⋯ that opens the row's menu, and keeps the cell at the table's right edge:
    ///     <c>Ui.RowActions.Menu().View(Routes.DriverPage(id)).Edit(Routes.EditDriverPage(id))</c>.
    /// </summary>
    public new bool? Menu { get; set; }

    /// <summary>Where the row is opened to be looked at: the menu's first item, an eye. Unset, there is none.</summary>
    public RouteUrl? View { get; set; }

    /// <summary>Where the pencil opens the row to be edited: <c>Ui.RowActions.Edit(Routes.EditSupplierPage(id))</c>. Unset, there is none.</summary>
    public RouteUrl? Edit { get; set; }

    /// <summary>
    ///     Runs when the reader asks to edit the row where it stands, rather than on a page of its own — the pencil, or the
    ///     menu's edit item, is then a button. It wins over <see cref="Edit" />.
    /// </summary>
    public Callback OnEdit { get; set; }

    /// <summary>Runs when the reader says yes to deleting the row. Unset, there is no bin.</summary>
    public Callback OnRemove { get; set; }

    /// <summary>The title over the bin's question — in the menu, the delete item's words too. "Delete" when unset.</summary>
    public string? RemoveHeading { get; set; }

    /// <summary>The question the bin asks first. "Are you sure you want to delete it?" when unset.</summary>
    public string? RemoveQuestion { get; set; }

    /// <summary>Classes for the call site, added to the cell's own.</summary>
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
    protected override Component? Render() => Menu == true ? Menued() : Pressables();

    // Flux's row action sits in a `py-0` cell, so the row keeps the height of its neighbours.
    private Component Pressables()
    {
        var remove = RaskStrings.Get(RaskString.RowActionsRemove, "Delete");

        return Ui.TableCell.End.Class("py-0", Class).Data("ui-row-actions", "")[
            Div.Class("[:where(&)]:flex justify-end [:where(&)]:gap-1")[
                Children is { } more ? Fragment[more] : null,
                View is { } view ? Pressed(Ui.IconName.Eye, RaskStrings.Get(RaskString.RowActionsView, "View")).Key("view").Href(view) : null,
                OnEdit.HasValue || Edit.HasValue ? Editing(Pressed(Ui.IconName.Pencil, RaskStrings.Get(RaskString.RowActionsEdit, "Edit")).Key("edit")) : null,
                OnRemove.HasValue ? UiRemoval.Asking(Pressed(Ui.IconName.Trash, remove).Key("remove"), OnRemove, RemoveHeading, RemoveQuestion, remove) : null
            ]
        ];
    }

    // Flux's table demo: `<flux:dropdown position="bottom" align="end" offset="-25">` round a ghost, small ⋯, in a `py-0`
    // cell; Flux's dropdown page puts the delete last, after a separator, in red. The cell is Flux's sticky one, on the
    // page's surface (Ui.Main's), at no weight so the page's own background wins.
    private Component Menued()
    {
        var items = Items();
        var cell = Ui.TableCell.End.Sticky().Class("py-0", UiTableBox.Surface, Class).Data("ui-row-actions", "menu");
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

    // The pencil edits where the row stands when the page listens, and opens the row's page otherwise.
    private UiButton Editing(UiButton pencil) => OnEdit.HasValue ? pencil.OnClick(OnEdit) : pencil.Href(Edit);

    private static UiButton Pressed(Ui.IconName icon, string word) =>
        Ui.Button.Ghost.Sm.Icon(icon).IconVariant(Ui.IconVariant.Outline).Class(Muted).Tooltip(word);
}
