using System.Text;

namespace Rask;

/// <summary>
/// One heading of a <see cref="UiTable" />: Flux's <c>flux:table.column</c>. It is the <c>&lt;th&gt;</c>.
/// </summary>
/// <remarks>
/// A sortable heading draws its label as a button with a chevron beside it. The table does not sort
/// anything: the page keeps which column is sorted and which way, says so with <see cref="Sorted" /> and
/// <see cref="Direction" />, and reorders its rows when <see cref="OnSort" /> fires.
/// </remarks>
public sealed partial class UiTableColumn : UiElement, IUiHost
{
    private const string Base = "border-b border-zinc-800/10 font-medium dark:border-white/20 "
        + "[:where(&)]:text-start [:where(&)]:text-sm [:where(&)]:text-zinc-800 dark:[:where(&)]:text-white";

    // The whole heading cell is the hover target, as it is the click target.
    private const string SortGroup = "group/sortable";

    private const string SortButton = "-my-1 -ms-2 flex items-center gap-1 px-2 py-1 text-start";

    private const string SortIcon = "rounded-sm text-zinc-400 group-hover/sortable:text-zinc-800 dark:group-hover/sortable:text-white";

    private const string SortHint = "opacity-0 group-hover/sortable:opacity-100";

    private static readonly UiPartMarker Marker = new("ui-column");

    /// <summary>Where the heading sits in its column. Start unless this says otherwise.</summary>
    public Ui.Align? Align { get; set; }

    /// <summary>Makes the heading a button that asks for this column to be sorted.</summary>
    public bool? Sortable { get; set; }

    /// <summary>Whether the rows are sorted by this column now: its chevron stays visible.</summary>
    public bool? Sorted { get; set; }

    /// <summary>Which way the sorted column runs. Ascending unless this says otherwise.</summary>
    public Ui.TableColumnDirection? Direction { get; set; }

    /// <summary>Keeps the column in view while the others scroll sideways under it.</summary>
    /// <remarks>
    ///     Give it a background — <c>.Class("bg-white dark:bg-zinc-900")</c> — and make the
    ///     <see cref="UiTableCell" />s under it sticky too.
    /// </remarks>
    public bool? Sticky { get; set; }

    /// <summary>Fired when a sortable heading is clicked, or its button is activated from the keyboard.</summary>
    /// <remarks>
    ///     It is the heading cell's click, which is where Flux's <c>wire:click</c> lands; a column that
    ///     also sets <c>OnClick</c> runs that first.
    /// </remarks>
    public Callback OnSort { get; set; }

    /// <inheritdoc />
    /// <remarks>None: it renders the <c>&lt;th&gt;</c> with its label wrapped inside.</remarks>
    protected override string? TagName => null;

    /// <inheritdoc />
    protected override string? ResolveClass() => UiClass.Compose(
        UiTableBox.Padding,
        Base,
        Sortable is true ? SortGroup : "",
        Sticky is true ? UiTableBox.Stuck : "",
        Class);

    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);

    /// <inheritdoc />
    protected override Component? Render()
    {
        var label = Div.Class(Label(Align));

        return HostedElement.Tag("th").Owner(this)[Sortable is true ? label[SortControl()] : label[Children ?? []]];
    }

    /// <inheritdoc />
    /// <remarks>The sort handler stands in for the click for the length of the walk, as the resolved ARIA does.</remarks>
    protected override void WriteAttributes(StringBuilder sb)
    {
        if (Sortable is not true || !OnSort.HasValue)
        {
            base.WriteAttributes(sb);
            return;
        }

        var click = OnClick;
        OnClick = click.HasValue ? new Callback<PointerEvent>(e => ClickThenSort(click, e)) : OnSort;
        try
        {
            base.WriteAttributes(sb);
        }
        finally
        {
            OnClick = click;
        }
    }

    void IUiHost.WriteHostAttributes(StringBuilder sb) => WriteAttributes(sb);

    private async Task ClickThenSort(Callback<PointerEvent> click, PointerEvent e)
    {
        await click.Invoke(e).ConfigureAwait(false);
        await OnSort.Invoke().ConfigureAwait(false);
    }

    private Component SortControl() =>
        Button.Type(ButtonType.Button).Class(SortButton).Data("ui-table-sortable", "")[
            Div[Children ?? []],
            Div.Class(SortIcon)[Indicator()]
        ];

    // A sorted column shows which way it runs; any other shows, on hover, the chevron a click would bring.
    private Component Indicator() =>
        Sorted is true
            ? Chevron(down: Direction is Ui.TableColumnDirection.Desc)
            : Div.Class(SortHint)[Chevron(down: true)];

    private static UiIcon Chevron(bool down) =>
        Ui.Icon.Name(down ? Ui.IconName.ChevronDown : Ui.IconName.ChevronUp).Variant(Ui.IconVariant.Micro);

    private static string Label(Ui.Align? align) => align switch
    {
        Ui.Align.Center => "flex justify-center",
        Ui.Align.End => "flex justify-end",
        _ => "flex",
    };
}
