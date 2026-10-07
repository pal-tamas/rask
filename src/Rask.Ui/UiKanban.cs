namespace Rask;

/// <summary>
///     Flux's <c>flux:kanban</c>: cards arranged in columns, one column per stage of a workflow.
/// </summary>
/// <remarks>
///     <para>
///     <c>Ui.Kanban[Ui.KanbanColumn[Ui.KanbanColumnHeader.Heading("Planned").Count(2), Ui.KanbanColumnCards[…]]]</c>.
///     It is the row the columns stand in, 16px apart, and nothing else: a column is 320px wide and does not
///     shrink, so a board wider than its place needs a scrolling box around it, which is the page's to give.
///     </para>
///     <para>
///     It draws a board and moves nothing, exactly as Flux's does: no card is draggable and no key reorders
///     one. A card that opens is <c>Ui.KanbanCard.As(Ui.KanbanCardAs.Button).OnClick(…)</c>; moving cards is
///     the page's own, with what it already uses to change a list.
///     </para>
/// </remarks>
public sealed partial class UiKanban : UiElement
{
    private static readonly UiPartMarker Marker = new("ui-kanban");

    /// <inheritdoc />
    protected override string TagName => "div";

    /// <inheritdoc />
    protected override string? ResolveClass() => UiClass.Compose("flex gap-4", Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);
}
