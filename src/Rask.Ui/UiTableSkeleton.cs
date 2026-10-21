using System.Globalization;
using System.Runtime.CompilerServices;
using Rask.Querying;

namespace Rask;

/// <summary>
///     The rows a table drawn from a query (<see cref="UiTable.Of{TResult}" />) shows while the query loads, and the
///     one it shows when the query found nothing.
/// </summary>
/// <remarks>
///     <para>
///         The first load has no rows to stand in for, so it draws a skeleton row per row the pager says the page
///         holds — the heading row stays, and every row is a real row's height, a <see cref="UiSkeletonLine" /> per
///         cell. A later load with nothing kept on screen draws as many as the table last showed.
///     </para>
///     <para>
///         Every later load is the browser's to show at the press (<c>rask-skeleton.ts</c>): the rows there keep
///         their places and draw bars. The box says <c>data-rask-skeleton="loading"</c> while the query has no rows
///         of its own on screen, which is how the browser knows to keep its skeleton past the answer to the press.
///     </para>
/// </remarks>
internal static class UiTableSkeleton
{
    /// <summary>What the box says while its query has no rows of its own on screen.</summary>
    internal const string Loading = "loading";

    // How many rows each query's table last showed, for a load that keeps nothing on screen meanwhile.
    private static readonly ConditionalWeakTable<IQueryState, StrongBox<int>> Shown = new();

    /// <summary>The box's <c>data-rask-skeleton</c>: <see cref="Loading" />, or empty when the rows are the query's.</summary>
    internal static string Word(IQueryState query) => query.IsLoading || query.IsPlaceholderData ? Loading : "";

    /// <summary>Puts skeleton rows, or the empty row, in the table's row group when the query calls for them.</summary>
    internal static void Fill(IQueryState query, IEnumerable<Component?>? parts, Component? pager)
    {
        UiTableRows? rows = null;
        var columns = 1;
        foreach (var part in parts ?? [])
        {
            if (part is UiTableRows body)
            {
                rows = body;
            }
            else if (part is UiTableColumns headings)
            {
                columns = Math.Max(1, Count<UiTableColumn>(headings.Children));
            }
        }

        if (rows is null)
        {
            return;
        }

        var shown = Shown.GetOrCreateValue(query);
        // A query holding the previous key's rows says it is loading too: those rows stay, and the sheet draws them.
        if (query.IsLoading && !query.IsPlaceholderData)
        {
            rows.Children = Placeholders(shown.Value > 0 ? shown.Value : PageRows(pager), columns);
            return;
        }

        shown.Value = Count<UiTableRow>(rows.Children);
        if (shown.Value == 0 && !query.IsPlaceholderData)
        {
            rows.Children = [Empty(columns)];
        }
    }

    // The rows the pager says this page holds: a page's worth, or what is left on the last one.
    private static int PageRows(Component? pager) => pager switch
    {
        UiPagination { Paginator: { Total: > 0 } counted } => counted.To - counted.From + 1,
        UiPagination { Paginator: var paginator } => Math.Max(paginator.PerPage, 1),
        _ => WithoutPager,
    };

    // A table with no pager under it: a page of UiPaginator's own default size.
    private const int WithoutPager = 15;

    private static Component[] Placeholders(int count, int columns) =>
    [
        .. Enumerable.Range(0, count).Select(row => Ui.TableRow.Key("ui-skeleton-" + row.ToString(CultureInfo.InvariantCulture))[
            Enumerable.Range(0, columns).Select(column => Ui.TableCell.Key(column)[Ui.SkeletonLine])
        ]),
    ];

    private static Component Empty(int columns) =>
        Ui.TableRow.Key("ui-empty")[
            Ui.TableCell.Class("text-center").Attributes(("colspan", columns.ToString(CultureInfo.InvariantCulture)))[
                RaskStrings.Get(RaskString.TableEmpty, "No results.")
            ]
        ];

    private static int Count<T>(IEnumerable<Component?>? parts) => parts?.Count(part => part is T) ?? 0;
}
