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
    // Flux's cell, in Flux's order: its box, size, alignment, tone, sticky edge, and the line above every row but the first.
    private const string Box = "[:where(&)]:py-3 [:where(&)]:px-3 first:ps-0 last:pe-0 text-sm";

    private const string Line = "not-in-[tr:first-child]:border-t border-zinc-800/10 dark:border-white/20";

    private const int Aligns = (int)Ui.Align.End + 1;

    private static readonly UiPartMarker Marker = new("ui-cell");

    // Every list a cell can write, built once: by variant, alignment and stickiness.
    private static readonly string[] Lists = [.. Enum.GetValues<Ui.TableCellVariant>().SelectMany(variant =>
        Enum.GetValues<Ui.Align>().SelectMany(align =>
            new[] { false, true }.Select(sticky => UiTableBox.Join(Box, Alignment(align), Tone(variant), sticky ? UiTableBox.Stuck : "", Line))))];

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
    /// <remarks>Flux's list as it is: a class of the call site's that contradicts it is written with <c>!</c> (<c>ps-10!</c>).</remarks>
    protected override string? ResolveClass() => UiTableBox.WithClass(List(Variant, Align, Sticky is true), Class);

    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);

    private static string List(Ui.TableCellVariant? variant, Ui.Align? align, bool sticky) =>
        Lists[((((int)(variant ?? default) * Aligns) + (int)(align ?? default)) * 2) + (sticky ? 1 : 0)];

    private static string Tone(Ui.TableCellVariant variant) => variant switch
    {
        Ui.TableCellVariant.Strong => "font-medium text-zinc-800 dark:text-white",
        _ => "text-zinc-500 dark:text-zinc-300",
    };

    private static string Alignment(Ui.Align align) => align switch
    {
        Ui.Align.Center => "text-center",
        Ui.Align.End => "text-end [&>*]:ms-auto",
        _ => "",
    };
}
