using System.Globalization;
using System.Text;

namespace Rask;

/// <summary>
///     The top of a <see cref="UiKanbanColumn" />: Flux's <c>flux:kanban.column.header</c>. What the stage is
///     called, how many cards it holds, and the buttons that act on it.
/// </summary>
/// <remarks>
///     <c>Ui.KanbanColumnHeader.Heading("Planned").Count(4).Actions(Ui.Button.Subtle.Sm.Icon(Ui.IconName.Plus))</c>.
///     Children take the place of <see cref="Heading" /> and <see cref="Count" />.
/// </remarks>
public sealed partial class UiKanbanColumnHeader : UiElement, IUiHost
{
    // 32px tall with or without actions, so a column that has buttons lines up with one that has none.
    private const string Row = "flex items-center justify-between min-h-8";

    private const string Title = "flex items-center gap-1.5 px-3";

    private const string SubheadingRow = "flex items-center gap-1.5 px-3 mb-1";

    private const string Muted = "text-sm text-zinc-500 dark:text-white/70";

    private static readonly UiPartMarker Marker = new("ui-kanban-column-header");

    private static readonly UiPartMarker SubheadingMarker = new("ui-subheading");

    /// <summary>What the column is called.</summary>
    public string? Heading { get; set; }

    /// <summary>A second line under the heading.</summary>
    public string? Subheading { get; set; }

    /// <summary>A number beside the heading: how many cards the column holds.</summary>
    public int? Count { get; set; }

    /// <summary>Buttons or a menu at the end of the heading's row: Flux's <c>actions</c> slot.</summary>
    public Component? Actions { get; set; }

    /// <inheritdoc />
    /// <remarks>None: it renders the header with its rows inside.</remarks>
    protected override string? TagName => null;

    /// <inheritdoc />
    protected override string? ResolveClass() => UiClass.Compose("flex flex-col p-2", Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);

    /// <inheritdoc />
    protected override Component? Render() =>
        HostedElement.Tag("div").Owner(this)[
            Div.Class(Row)[
                Div.Class(Title)[Children?.Any() == true ? Children : Named()],
                Div.Class("flex items-center gap-1")[Actions]
            ],
            Subheading is null ? null : Div.Class(SubheadingRow)[Div.Class(Muted).Data(SubheadingMarker.With(null))[Subheading]]
        ];

    void IUiHost.WriteHostAttributes(StringBuilder sb) => WriteAttributes(sb);

    private IEnumerable<Component?> Named() =>
    [
        Heading is null ? null : Ui.Heading[Heading],
        Count is { } count ? Div.Class(Muted)[count.ToString(CultureInfo.InvariantCulture)] : null,
    ];
}
