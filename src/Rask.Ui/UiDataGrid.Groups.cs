using System.Globalization;
using System.Linq.Expressions;

namespace Rask;

public sealed partial class UiDataGrid<T, TKey>
{
    // Bands, drawn by recursion over the grouping levels.
    //
    // Recursive rather than a single pass with a running "where did this band start" index, and that is
    // a correctness point rather than a stylistic one. With two levels the OUTER subtotal has to span
    // every inner band beneath it while the inner one spans only its own run, and one start index cannot
    // say both — it reports the outer total as the last inner band's. Recursing on the parent band's own
    // slice also means an inner run is compared only against its siblings, so two bands that happen to
    // share an inner key under different parents stay apart instead of merging into one.
    private IEnumerable<Component?> Bands(
        IReadOnlyList<UiColumn<T>> visible, IReadOnlyList<UiColumn<T>> groups, IReadOnlyList<T> rows,
        int span) =>
        Band(visible, groups, rows, span, 0, []);

    private IEnumerable<Component?> Band(
        IReadOnlyList<UiColumn<T>> visible, IReadOnlyList<UiColumn<T>> groups, IReadOnlyList<T> rows,
        int span, int level, IReadOnlyList<object?> above)
    {
        // Past the last grouped column: what is left is ordinary rows.
        if (level == groups.Count)
        {
            foreach (var component in Rows(visible, rows, span))
            {
                yield return component;
            }

            yield break;
        }

        var column = groups[level];
        var at = 0;

        while (at < rows.Count)
        {
            // The rows arrive already ordered by every group key (see SortInMemory), so a band is a run
            // of adjacent rows sharing this level's key — no dictionary, and the run keeps the order the
            // reader sees.
            var key = column.Band(rows[at]);
            var end = at;
            while (end < rows.Count && Equals(column.Band(rows[end]), key))
            {
                end++;
            }

            var band = Slice(rows, at, end);
            var keys = new List<object?>(above) { key };
            var path = Path(keys, keys.Count);

            yield return BandHeader(column, key, band, path, level, span);

            if (!(GroupCollapsible is not false && _collapsed.Contains(path)))
            {
                foreach (var component in Band(visible, groups, band, span, level + 1, keys))
                {
                    yield return component;
                }

                if (GroupSubtotals is true)
                {
                    yield return Subtotal(visible, band, level);
                }
            }

            at = end;
        }
    }

    // A band is addressed by the keys above it, joined — so a collapsed "Europe / France" survives a
    // re-render and does not collapse "Asia / France" with it. A unit separator rather than a printable
    // one, so a key that happens to contain the separator cannot forge another band's path.
    private static string Path(List<object?> keys, int depth)
    {
        var parts = new string[depth];
        for (var i = 0; i < depth; i++)
        {
            parts[i] = keys[i]?.ToString() ?? "";
        }

        return string.Join('\u001f', parts);
    }

    private static List<T> Slice(IReadOnlyList<T> rows, int from, int to)
    {
        var slice = new List<T>(Math.Max(to - from, 0));
        for (var i = from; i < to && i < rows.Count; i++)
        {
            slice.Add(rows[i]);
        }

        return slice;
    }

    private Component BandHeader(
        UiColumn<T> column, object? key, List<T> band, string path, int level, int span)
    {
        var collapsed = _collapsed.Contains(path);
        var heading = column.GroupHeader is { } custom && custom.Invoke(key, band) is { } drawn
            ? drawn
            : Span.Class("font-medium")[
                (column.Title ?? "") + ": " + (key?.ToString() ?? "—")
                + " (" + band.Count.ToString(CultureInfo.InvariantCulture) + ")"
            ];

        return Tr.Key("band-" + path)[
            Td.ColSpan(span).Class("bg-base-200")[
                Div.Class("flex items-center gap-2").Style("padding-inline-start:" + level + "rem")[
                    BandToggle(path, collapsed),
                    heading
                ]
            ]
        ];
    }

    private UiButton? BandToggle(string path, bool collapsed)
    {
        if (GroupCollapsible is false)
        {
            return null;
        }

        return Ui.Button.Ghost.Xs.Icon(collapsed ? Ui.IconName.ChevronRight : Ui.IconName.ChevronDown)
            .AriaLabel(collapsed
                ? RaskStrings.Get(RaskString.DataGridExpandGroup, "Expand group")
                : RaskStrings.Get(RaskString.DataGridCollapseGroup, "Collapse group"))
            .OnClick(() => ToggleBand(path));
    }

    private Component Subtotal(IReadOnlyList<UiColumn<T>> visible, List<T> band, int level) =>
        Tr.Key("subtotal-" + level + "-" + band.Count)[
            LeadingCells > 0 ? Td.ColSpan(LeadingCells).Class("bg-base-100") : null,
            visible.Select(column =>
                Td.Key(column.FieldName ?? column.Title ?? "")
                    .Class(UiClass.Compose("bg-base-100 font-medium", column.Class))[
                    column.HasFooter ? column.Foot(band) : (Component)""
                ])
        ];
}
