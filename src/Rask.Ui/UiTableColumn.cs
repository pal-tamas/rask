using System.Text;

namespace Rask;

/// <summary>
/// One heading of a <see cref="UiTable" />: Flux's <c>flux:table.column</c>. It is the <c>&lt;th&gt;</c>.
/// </summary>
/// <remarks>
/// A sortable heading draws its label as a button with a chevron beside it — after the label, or before
/// it when the heading is aligned to the end, so the label stays by that edge. The table does not sort
/// anything: the page keeps which column is sorted and which way, says so with <see cref="Sorted" /> and
/// <see cref="Direction" />, and reorders its rows when <see cref="OnSort" /> fires.
/// </remarks>
public sealed partial class UiTableColumn : UiElement, IUiHost
{
    // Flux's heading, in Flux's order: its box and look, the group its alignment names, the sortable group, the sticky
    // edge, and the sort button's own margin taken back where it ends the heading.
    private const string Box = "py-3 px-3 first:ps-0 last:pe-0 text-start text-sm font-medium text-zinc-800 dark:text-white "
        + "border-b border-zinc-800/10 dark:border-white/20";

    private const string SortGroup = "group/sortable";

    private const string SortEdge = "**:data-ui-table-sortable:last:me-0";

    // The label and the sort button follow the heading's alignment group, so neither has a list per alignment.
    private const string Label = @"flex in-[.group\/center-align]:justify-center in-[.group\/end-align]:justify-end";

    // An end-aligned heading draws its chevron before the label, hung into the space on that side.
    private const string SortButton = "flex items-center gap-1 -my-1 -ms-2 -me-2 px-2 py-1 "
        + "[text-align:inherit] [text-indent:inherit] [text-shadow:inherit] [text-transform:inherit] [word-spacing:inherit] "
        + @"in-[.group\/end-align]:flex-row-reverse in-[.group\/end-align]:-me-2 in-[.group\/end-align]:-ms-8";

    private const string SortIcon = "rounded-sm text-zinc-400 group-hover/sortable:text-zinc-800 dark:group-hover/sortable:text-white";

    private const string SortHint = "opacity-0 group-hover/sortable:opacity-100";

    // Every list a heading can write, built once: by alignment, sortability and stickiness.
    private static readonly string[] Lists = [.. Enum.GetValues<Ui.Align>().SelectMany(align =>
        new[] { false, true }.SelectMany(sortable => new[] { false, true }.Select(sticky =>
            UiTableBox.Join(Box, Group(align), sortable ? SortGroup : "", sticky ? UiTableBox.Stuck : "", SortEdge))))];

    private static readonly UiPartMarker Marker = new("ui-column");

    // A sortable heading waits on its own sort like a button: the runtime marks the cell that carries the
    // click, and the table dims its rows by that mark (ui.css).
    private static readonly UiPartMarker Waiting = Marker.And("rask-loading");

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
    /// <remarks>Flux's list as it is: a class of the call site's that contradicts it is written with <c>!</c> (<c>ps-10!</c>).</remarks>
    protected override string? ResolveClass() =>
        UiTableBox.WithClass(Lists[((int)(Align ?? default) * 4) + (Sortable is true ? 2 : 0) + (Sticky is true ? 1 : 0)], Class);

    private protected override IReadOnlyDictionary<string, string?> ResolveData() =>
        (Sortable is true ? Waiting : Marker).With(Data);

    /// <inheritdoc />
    protected override Component? Render()
    {
        var label = Div.Class(Label);

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

    private static string Group(Ui.Align align) => align switch
    {
        Ui.Align.Center => "group/center-align",
        Ui.Align.End => "group/end-align",
        _ => "",
    };
}
