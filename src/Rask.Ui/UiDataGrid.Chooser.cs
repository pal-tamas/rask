using System.Linq.Expressions;
using Rask.Core.DragAndDrop;

namespace Rask;

public sealed partial class UiDataGrid<T, TKey>
{
    // The column chooser and the group panel, wrapped in the framework's headless drag-and-drop so both
    // can also be reordered by dragging.
    //
    // BUTTONS ARE THE MECHANISM AND DRAG IS THE BONUS, which is a decision about who can use it rather
    // than about taste: HTML5 drag events do not fire on touch at all and cannot be driven from a
    // keyboard, so a panel whose only gesture was dragging would be unreachable on the phone this kit
    // designs for first. Both paths call the same handler, so there is one behaviour to test.
    private DragDrop? Chrome(IReadOnlyList<UiColumn<T>> columns, IReadOnlyList<UiColumn<T>> groups)
    {
        var chooser = ColumnChooser is true;
        var panel = GroupPanel is true;
        if (!chooser && !panel)
        {
            return null;
        }

        return DragDrop
            .Body(ctx => Div.Class("flex flex-wrap items-start gap-2")[
                chooser ? ChooserBar(columns, ctx) : null,
                panel ? Panel(groups, ctx) : null
            ])
            .OnDrop(move => DropAsync(move, columns));
    }

    // One drop, routed by the zone it landed in. The index is the position within that zone's own list,
    // which for the chooser is the effective column order and for the panel is the grouping.
    private Task DropAsync(DragDropMove move, IReadOnlyList<UiColumn<T>> columns)
    {
        if (!string.Equals(move.FromZone, move.ToZone, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        if (string.Equals(move.ToZone, ColumnZone, StringComparison.Ordinal))
        {
            var order = EffectiveOrder(columns);
            move.ApplyTo(order);
            return SetOrderAsync(order);
        }

        var grouped = new List<string>(CurrentGrouped);
        move.ApplyTo(grouped);
        return SetGroupedAsync(grouped);
    }

    private const string ColumnZone = "columns";

    private const string GroupZone = "groups";

    private Component ChooserBar(IReadOnlyList<UiColumn<T>> columns, DragDropContext ctx) =>
        Div.Class("relative")[
            Ui.Button.Sm.Icon(Ui.IconName.Bars3)
                .OnClick(() => _chooserOpen = !_chooserOpen)[RaskStrings.Get(RaskString.DataGridColumns, "Columns")],
            !_chooserOpen
                ? null
                : Div
                    .Class("absolute z-10 mt-1 flex w-64 flex-col gap-1 rounded-box border "
                        + "border-base-300 bg-base-100 p-2 shadow-lg")
                    .AriaLabel(RaskStrings.Get(RaskString.DataGridColumns, "Columns"))
                    .Role("group")[ChooserRows(columns, ctx)]
        ];

    private IEnumerable<Component?> ChooserRows(IReadOnlyList<UiColumn<T>> columns, DragDropContext ctx)
    {
        var order = EffectiveOrder(columns);
        for (var i = 0; i < order.Count; i++)
        {
            var token = order[i];
            var column = Find(columns, token);

            // A column with no field token has no name to address it by, so it can be neither hidden nor
            // moved — it simply is not listed.
            if (column is null)
            {
                continue;
            }

            var index = i;
            var hidden = CurrentHidden.Contains(token, StringComparer.Ordinal);

            yield return Div
                .Key(token)
                .Class(UiClass.Compose(
                    "flex items-center gap-1 rounded px-1",
                    ctx.IsDropTarget(ColumnZone, index) ? "bg-base-200" : ""))
                .Draggable(column.CanReorder && ReorderEnabled)
                .OnDragStart(ctx.DragStart(ColumnZone, index))
                .OnDragOver(ctx.DragOver(ColumnZone, index))
                .OnDrop(ctx.Drop(ColumnZone, index))
                .OnDragEnd(ctx.DragEnd)[
                column.CanReorder && ReorderEnabled
                    ? Ui.Icon.Name(Ui.IconName.EllipsisVertical).Class("size-3 shrink-0 opacity-40")
                    : null,
                // RaskMarkup.Label, not Label: this grid has a Label PROPERTY, and a component's own
                // member hides the injected entry of the same name — the rule that gives every kit
                // component its Ui prefix. Naming the base the entry lives on reaches the tag again.
                RaskMarkup.Label.Class("flex flex-1 items-center gap-2 text-sm")[
                    Input
                        .Of<bool>()
                        .Checked(!hidden)
                        .OnChange(_ => ToggleHiddenAsync(token))
                        .Class("checkbox checkbox-xs")
                        .Disabled(!column.CanHide),
                    column.Title ?? token
                ],
                MoveButton(column.CanReorder && ReorderEnabled && index > 0, RaskStrings.Get(RaskString.DataGridMoveUp, "Move up"),
                    Ui.IconName.ChevronUp, () => MoveColumnAsync(columns, token, -1)),
                MoveButton(column.CanReorder && ReorderEnabled && index < order.Count - 1, RaskStrings.Get(RaskString.DataGridMoveDown, "Move down"),
                    Ui.IconName.ChevronDown, () => MoveColumnAsync(columns, token, 1))
            ];
        }
    }

    private Component Panel(IReadOnlyList<UiColumn<T>> groups, DragDropContext ctx) =>
        Div
            .Class("flex flex-wrap items-center gap-2 rounded-box border border-dashed "
                + "border-base-300 p-2")
            .AriaLabel(RaskStrings.Get(RaskString.DataGridGrouping, "Grouping"))
            .Role("group")[
            // The chips as a sequence rather than wrapped in a Fragment: Fragment is internal to
            // Rask.Core, so its entry is private protected and no other assembly can name it. The
            // enumerable indexer takes the sequence directly, which is what Fragment would have done.
            groups.Count == 0
                ? [Span.Class("text-sm text-base-content/60")[
                    RaskStrings.Get(RaskString.DataGridGroupingHint, "Group by a column with its header button.")
                ]]
                : GroupChips(groups, ctx)
        ];

    private IEnumerable<Component?> GroupChips(IReadOnlyList<UiColumn<T>> groups, DragDropContext ctx)
    {
        for (var i = 0; i < groups.Count; i++)
        {
            var column = groups[i];
            var token = column.FieldName!;
            var index = i;

            yield return Div
                .Key(token)
                .Class(UiClass.Compose(
                    "flex items-center gap-1 rounded-full bg-base-200 py-1 pe-1 ps-2 text-sm",
                    ctx.IsDropTarget(GroupZone, index) ? "ring-2 ring-primary" : ""))
                .Draggable()
                .OnDragStart(ctx.DragStart(GroupZone, index))
                .OnDragOver(ctx.DragOver(GroupZone, index))
                .OnDrop(ctx.Drop(GroupZone, index))
                .OnDragEnd(ctx.DragEnd)[
                Ui.Icon.Name(Ui.IconName.EllipsisVertical).Class("size-3 shrink-0 opacity-40"),
                Span[column.Title ?? token],
                MoveButton(index > 0, RaskStrings.Get(RaskString.DataGridMoveGroupLeft, "Move group left"), Ui.IconName.ArrowLeft,
                    () => MoveGroupAsync(token, -1)),
                MoveButton(index < groups.Count - 1, RaskStrings.Get(RaskString.DataGridMoveGroupRight, "Move group right"), Ui.IconName.ArrowRight,
                    () => MoveGroupAsync(token, 1)),
                MoveButton(true, RaskStrings.Get(RaskString.DataGridUngroup, "Stop grouping by {0}", column.Title ?? token), Ui.IconName.XMark,
                    () => UngroupAsync(token))
            ];
        }
    }

    private static UiButton? MoveButton(bool enabled, string label, Ui.IconName icon, Func<Task> click) =>
        Ui.Button.Ghost.Xs.Icon(icon)
            .AriaLabel(label)
            .Disabled(!enabled)
            .OnClick(click);

    private static UiColumn<T>? Find(IReadOnlyList<UiColumn<T>> columns, string token) =>
        columns.FirstOrDefault(column => string.Equals(column.FieldName, token, StringComparison.Ordinal));
}
