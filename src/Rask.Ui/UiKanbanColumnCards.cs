namespace Rask;

/// <summary>
///     The cards of a <see cref="UiKanbanColumn" />, one under the other: Flux's <c>flux:kanban.column.cards</c>.
/// </summary>
public sealed partial class UiKanbanColumnCards : UiElement
{
    private static readonly UiPartMarker Marker = new("ui-kanban-column-cards");

    /// <inheritdoc />
    protected override string TagName => "div";

    /// <inheritdoc />
    protected override string? ResolveClass() => UiClass.Compose("flex flex-col gap-2 px-2 pb-2", Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);
}
