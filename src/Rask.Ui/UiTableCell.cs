using System.Text;

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
    // Flux's cell, less what every cell shares: its box, size, muted tone and the line above every row but the first
    // are the kit sheet's, once (ui.css, `[data-ui-cell]`). A cell writes what differs: its alignment, a strong
    // tone, a sticky edge.
    private const int Aligns = (int)Ui.Align.End + 1;

    private static readonly UiPartMarker Marker = new("ui-cell");

    // A value too long for its column ends in an ellipsis on one line; the cell's title says the whole of it.
    private const string Cuts = "truncate";

    // Every list a cell can write, built once: by variant, alignment, stickiness and whether it cuts its value short.
    // Null where there is nothing to write.
    private static readonly string?[] Lists = [.. Enum.GetValues<Ui.TableCellVariant>().SelectMany(variant =>
        Enum.GetValues<Ui.Align>().SelectMany(align =>
            new[] { false, true }.SelectMany(sticky => new[] { false, true }.Select(cut =>
                UiTableBox.Join(Alignment(align), Tone(variant), sticky ? UiTableBox.Stuck : "", cut ? Cuts : "")))))];

    /// <summary>Where the value sits in its column. Start unless this says otherwise.</summary>
    public Ui.Align? Align { get; set; }

    /// <summary>How much the cell stands out. Muted unless this says otherwise.</summary>
    public Ui.TableCellVariant? Variant { get; set; }

    /// <summary>Keeps the cell in view while the others scroll sideways under it.</summary>
    /// <remarks>Give it a background as well — <c>.Class("bg-white dark:bg-zinc-900")</c>.</remarks>
    public bool? Sticky { get; set; }

    /// <summary>
    ///     The cell's value as text, on one line: cut short with an ellipsis where the column ends, and the whole of it in
    ///     the cell's tooltip. <c>Ui.TableCell.Cut(supplier.Name)</c>.
    /// </summary>
    /// <remarks>
    ///     Not Flux's: an addition the owner approved (2026-11-04), for the columns a <see cref="UiTableColumn.Width" />
    ///     holds still. A cell given children as well shows them, and the text is its tooltip. A <c>Title</c> of the call
    ///     site's wins. On paper the whole value is printed, wrapped.
    /// </remarks>
    public string? Cut { get; set; }

    /// <inheritdoc />
    protected override string TagName => "td";

    /// <inheritdoc />
    /// <remarks>What this cell writes beyond the shared look, then the call site's class, which wins over that look.</remarks>
    protected override string? ResolveClass() => UiTableBox.WithClass(List(Variant, Align, Sticky is true, Cut is not null), Class);

    /// <inheritdoc />
    /// <remarks>A cut value is the cell's title for the length of the walk, unless the call site wrote one.</remarks>
    protected override void WriteAttributes(StringBuilder sb)
    {
        if (Cut is null || Title is not null)
        {
            base.WriteAttributes(sb);
            return;
        }

        Title = Cut;
        try
        {
            base.WriteAttributes(sb);
        }
        finally
        {
            Title = null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The serializer walks an indexer's array as it stands and asks this only when there is none: a cell given
    ///     nothing but <see cref="Cut" /> shows that text.
    /// </remarks>
    protected override IEnumerable<Component?> RenderChildren() =>
        Children is null && Cut is { } text ? [(Component)text] : base.RenderChildren();

    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);

    private static string? List(Ui.TableCellVariant? variant, Ui.Align? align, bool sticky, bool cut) =>
        Lists[((((((int)(variant ?? default) * Aligns) + (int)(align ?? default)) * 2) + (sticky ? 1 : 0)) * 2) + (cut ? 1 : 0)];

    private static string Tone(Ui.TableCellVariant variant) => variant switch
    {
        Ui.TableCellVariant.Strong => "font-medium text-zinc-800 dark:text-white",
        _ => "",
    };

    private static string Alignment(Ui.Align align) => align switch
    {
        Ui.Align.Center => "text-center",
        Ui.Align.End => "text-end [&>*]:ms-auto",
        _ => "",
    };
}
