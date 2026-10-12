namespace Rask;

/// <summary>
/// One value of a <see cref="UiTableRow" />: Flux's <c>flux:table.cell</c>. It is the <c>&lt;td&gt;</c>.
/// </summary>
/// <remarks>
/// A cell that holds a control taller than a line of text — a badge, a button — drops its own vertical
/// padding with <c>.Class("py-0")</c>, so the row keeps the height of its neighbours.
/// </remarks>
public sealed partial class UiTableCell : UiElement
{
    // Every row but the first draws the line above it.
    private const string Base =
        "border-zinc-800/10 [:where(&)]:text-sm dark:border-white/20 [[data-ui-row]:not(:first-child)>&]:border-t";

    private static readonly UiPartMarker Marker = new("ui-cell");

    /// <summary>Where the value sits in its column. Start unless this says otherwise.</summary>
    public Ui.Align? Align { get; set; }

    /// <summary>How much the cell stands out. Muted unless this says otherwise.</summary>
    public Ui.TableCellVariant? Variant { get; set; }

    /// <summary>Keeps the cell in view while the others scroll sideways under it.</summary>
    /// <remarks>Give it a background as well — <c>.Class("bg-white dark:bg-zinc-900")</c>.</remarks>
    public bool? Sticky { get; set; }

    /// <inheritdoc />
    protected override string TagName => "td";

    /// <inheritdoc />
    protected override string? ResolveClass() => UiClass.Compose(
        UiTableBox.Padding,
        Base,
        Tone(Variant),
        Alignment(Align),
        Sticky is true ? UiTableBox.Stuck : "",
        Class);

    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);

    private static string Tone(Ui.TableCellVariant? variant) => variant switch
    {
        Ui.TableCellVariant.Strong => "font-medium [:where(&)]:text-zinc-800 dark:[:where(&)]:text-white",
        _ => "[:where(&)]:text-zinc-500 dark:[:where(&)]:text-zinc-300",
    };

    private static string Alignment(Ui.Align? align) => align switch
    {
        Ui.Align.Center => "[:where(&)]:text-center",
        Ui.Align.End => "[:where(&)]:text-end",
        _ => "",
    };
}
