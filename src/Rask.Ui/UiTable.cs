using System.Text;

namespace Rask;

/// <summary>
/// A table of data: Flux's <c>flux:table</c>. Its parts are <see cref="UiTableColumns" /> holding one
/// <see cref="UiTableColumn" /> per heading, and <see cref="UiTableRows" /> holding a
/// <see cref="UiTableRow" /> of <see cref="UiTableCell" />s per record.
/// </summary>
/// <remarks>
/// <para>
/// It renders a column box, a scroll area inside it, and the <c>&lt;table&gt;</c> inside that: a table wider
/// or taller than its box scrolls there instead of pushing the page. Everything an element takes —
/// <c>Id</c>, <c>Class</c>, <c>Data</c>, <c>Aria</c>, the events — lands on the <c>&lt;table&gt;</c>; the
/// box takes <see cref="ContainerClass" />.
/// </para>
/// <para>
/// A cell does not wrap, so a long value widens its column; write <c>whitespace-normal</c> on the cell that
/// should.
/// </para>
/// </remarks>
public sealed partial class UiTable : UiElement, IUiHost
{
    private const string Base = "isolate min-w-full table-fixed border-separate border-spacing-0 whitespace-nowrap text-zinc-800";

    // The pager under the rows keeps its height when the box is given one to scroll within, as Flux's does.
    private const string Container = "flex flex-col *:data-ui-pagination:shrink-0";

    // The card's bleed contract (UiCard, UiCardBody): --ui-bleed sideways, and — where the table is the first
    // or last thing in the box — --ui-bleed-top / -bottom to that edge, rounded by --ui-bleed-*-radius. A box
    // that states only --ui-bleed, or nothing, bleeds sideways alone.
    private const string Bleeding =
        "-mx-[var(--ui-bleed,1.5rem)] "
        + "first:-mt-[var(--ui-bleed-top,0px)] first:rounded-t-[var(--ui-bleed-top-radius,0px)] "
        + "last:-mb-[var(--ui-bleed-bottom,0px)] last:rounded-b-[var(--ui-bleed-bottom-radius,0px)]";

    private const string ScrollArea = "block overflow-auto";

    private static readonly UiPartMarker Marker = new("ui-table");

    /// <summary>
    ///     Runs the row dividers through the horizontal padding of the box the table sits in, while the first
    ///     and last columns stay in line with that box's content.
    /// </summary>
    /// <remarks>
    ///     The distance is <c>--ui-bleed</c>, 1.5rem unless the box says otherwise: a box of yours states it
    ///     beside its padding — <c>p-4 [--ui-bleed:1rem]</c>. A <see cref="UiCard" /> states it and the rest of
    ///     its bleed variables, so a table that opens or closes a card or its body reaches that edge too.
    /// </remarks>
    public bool? Bleed { get; set; }

    /// <summary>The pager for this table's rows, shown under them and outside the scroll area.</summary>
    public Component? Paginate { get; set; }

    /// <summary>Classes for the box around the table — a height to scroll within, such as <c>max-h-80</c>.</summary>
    public string? ContainerClass { get; set; }

    /// <inheritdoc />
    /// <remarks>None: the table renders as its box, with the <c>&lt;table&gt;</c> inside.</remarks>
    protected override string? TagName => null;

    /// <inheritdoc />
    protected override string? ResolveClass() => UiClass.Compose(Base, Class);

    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);

    /// <inheritdoc />
    protected override Component? Render()
    {
        var box = Div.Class(UiClass.Compose(Container, Bleed is true ? Bleeding : "", ContainerClass));

        return (Bleed is true ? box.Data("ui-table-bleed", "") : box)[
            Div.Class(ScrollArea)[
                HostedElement.Tag("table").Owner(this)[Children ?? []]
            ],
            Paginate
        ];
    }

    void IUiHost.WriteHostAttributes(StringBuilder sb) => WriteAttributes(sb);
}
