namespace Rask;

/// <summary>
///     What each <see cref="Ui.ColumnWidth" /> makes of a heading, and of the table it heads: the heading's class, the
///     rem it counts towards the table's least width, and the edge its values keep to.
/// </summary>
internal static class UiColumnWidths
{
    /// <summary>A table whose columns have widths is never narrower than this many rem.</summary>
    internal const int TableAtLeast = 40;

    // Every width wraps its label, hyphenated, rather than spilling over, and gives way on paper. A width is the kit's
    // default, so the call site's own `w-52` wins over it.
    private const string Wraps = " print:w-auto whitespace-normal hyphens-auto break-words";

    /// <summary>The heading's class for <paramref name="width" />, written whole so Tailwind reads it.</summary>
    internal static string Class(Ui.ColumnWidth width) => width switch
    {
        Ui.ColumnWidth.Day or Ui.ColumnWidth.Number => "[:where(&)]:w-28" + Wraps,
        Ui.ColumnWidth.Moment or Ui.ColumnWidth.Short or Ui.ColumnWidth.Reference => "[:where(&)]:w-40" + Wraps,
        Ui.ColumnWidth.Name => "[:where(&)]:w-72" + Wraps,
        Ui.ColumnWidth.Flag => "[:where(&)]:w-20" + Wraps,
        Ui.ColumnWidth.Actions => "[:where(&)]:w-24" + Wraps,
        Ui.ColumnWidth.Buttons => "[:where(&)]:w-48" + Wraps,
        // Held at the edge (UiTableColumn makes it sticky), on the page's surface so the columns beneath do not show through.
        Ui.ColumnWidth.RowMenu => "[:where(&)]:w-16 " + UiTableBox.Surface + Wraps,
        // The rest takes what the others leave; on paper it keeps room enough that only its longest values wrap.
        _ => "print:min-w-60" + Wraps,
    };

    /// <summary>How many rem <paramref name="width" /> counts towards the table's least width.</summary>
    internal static int Rem(Ui.ColumnWidth width) => width switch
    {
        Ui.ColumnWidth.Day or Ui.ColumnWidth.Number => 7,
        Ui.ColumnWidth.Moment or Ui.ColumnWidth.Short or Ui.ColumnWidth.Reference => 10,
        Ui.ColumnWidth.Name => 18,
        Ui.ColumnWidth.Flag => 5,
        Ui.ColumnWidth.Actions => 6,
        Ui.ColumnWidth.Buttons => 12,
        Ui.ColumnWidth.RowMenu => 4,
        _ => 12,
    };

    /// <summary>Numbers and the row's actions keep to the column's end; everything else to its start.</summary>
    internal static Ui.Align Align(Ui.ColumnWidth width) =>
        width is Ui.ColumnWidth.Number or Ui.ColumnWidth.Actions or Ui.ColumnWidth.Buttons or Ui.ColumnWidth.RowMenu ? Ui.Align.End : Ui.Align.Start;

    /// <summary>Whether a heading of <paramref name="width" /> heads the row actions, and says so to a screen reader when it has no words.</summary>
    internal static bool HeadsActions(Ui.ColumnWidth width) => width is Ui.ColumnWidth.Actions or Ui.ColumnWidth.Buttons or Ui.ColumnWidth.RowMenu;

    /// <summary>Whether a heading of <paramref name="width" /> keeps to the table's edge whether or not it says <c>Sticky</c>: the row's ⋯.</summary>
    internal static bool Sticks(Ui.ColumnWidth? width) => width is Ui.ColumnWidth.RowMenu;

    /// <summary>
    ///     The least width, in rem, of a table headed by <paramref name="columns" />: what they take together, a column with
    ///     no width counted as <see cref="Ui.ColumnWidth.Rest" />, and never under <see cref="TableAtLeast" />. Null when no
    ///     heading names a width: the table is Flux's, sized by what it holds.
    /// </summary>
    internal static int? Least(IEnumerable<UiTableColumn> columns)
    {
        var any = false;
        var total = 0;
        foreach (var width in columns.Select(column => column.Width))
        {
            any |= width.HasValue;
            total += Rem(width ?? Ui.ColumnWidth.Rest);
        }

        return any ? Math.Max(TableAtLeast, total) : null;
    }
}
