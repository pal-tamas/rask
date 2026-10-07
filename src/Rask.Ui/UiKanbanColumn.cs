using System.Text;

namespace Rask;

/// <summary>
///     One stage of a <see cref="UiKanban" />: Flux's <c>flux:kanban.column</c>. A <see cref="UiKanbanColumnHeader" />,
///     a <see cref="UiKanbanColumnCards" /> and optionally a <see cref="UiKanbanColumnFooter" />.
/// </summary>
/// <remarks>
///     The tinted panel is as tall as what is in it, inside a column as tall as the board's tallest: a short
///     column ends where its cards do. It works on its own too, outside a board.
/// </remarks>
public sealed partial class UiKanbanColumn : UiElement, IUiHost
{
    private const string Panel = "w-80 max-w-80 rounded-lg bg-zinc-100 dark:bg-zinc-800";

    private static readonly UiPartMarker Marker = new("ui-kanban-column");

    /// <inheritdoc />
    /// <remarks>None: it renders the column with its panel inside.</remarks>
    protected override string? TagName => null;

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);

    /// <inheritdoc />
    protected override Component? Render() =>
        HostedElement.Tag("div").Owner(this)[Div.Class(Panel)[Children ?? []]];

    void IUiHost.WriteHostAttributes(StringBuilder sb) => WriteAttributes(sb);
}
