namespace Rask;

/// <summary>
///     What sits under a column's cards — a "New card" button, a form: Flux's <c>flux:kanban.column.footer</c>.
/// </summary>
public sealed partial class UiKanbanColumnFooter : UiElement
{
    private static readonly UiPartMarker Marker = new("ui-kanban-column-footer");

    /// <inheritdoc />
    protected override string TagName => "div";

    /// <inheritdoc />
    protected override string? ResolveClass() => UiClass.Compose("[:where(&)]:flex flex-col [:where(&)]:gap-2 [:where(&)]:px-2 [:where(&)]:pb-2", Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);
}
