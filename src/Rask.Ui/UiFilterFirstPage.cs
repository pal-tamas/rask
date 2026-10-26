using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     How a filter change takes the list a <see cref="UiFilters" /> drives back to its first page: the page the table's
///     pager is on is no page of the narrowed list. The sort, and every other key, stays.
/// </summary>
/// <remarks>
///     A pager of links says where its page is kept: the keys its first two pages' addresses disagree on. A pager of
///     buttons is told it is on page one.
/// </remarks>
internal static class UiFilterFirstPage
{
    /// <summary>What to write into the address beside a filter, so the list starts at its first page.</summary>
    /// <param name="pager">The pager under the table the set drives, if it has one.</param>
    internal static KeyValuePair<string, string?>[] Keys(UiPagination? pager) =>
        pager is { Href: { HasValue: true } href, Paginator.Page: not 1 } ? Keys(href.Invoke(1), href.Invoke(2)) : [];

    /// <summary>
    ///     The keys the address of the first page and the second disagree on, each set as the first page writes it —
    ///     taken out where the first page leaves it out.
    /// </summary>
    /// <param name="first">Where the first page lives.</param>
    /// <param name="second">Where the second page lives.</param>
    internal static KeyValuePair<string, string?>[] Keys(RouteUrl first, RouteUrl second)
    {
        var one = Query(first);
        var two = Query(second);

        return
        [
            .. two.Keys.Union(one.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(key => !string.Equals(Value(one, key), Value(two, key), StringComparison.Ordinal))
                .Select(key => new KeyValuePair<string, string?>(key, Value(one, key))),
        ];
    }

    /// <summary>Tells a pager of buttons that is past its first page to go to it.</summary>
    /// <param name="pager">The pager under the table the set drives, if it has one.</param>
    internal static Task Turn(UiPagination? pager) =>
        pager is { Href: not { HasValue: true }, OnPage.HasValue: true, Paginator.Page: not 1 }
            ? pager.OnPage.Invoke(1).AsTask()
            : Task.CompletedTask;

    // The query of an address, generated (its own query string) or written as text (after its `?`, before any `#`).
    private static QueryCollection Query(RouteUrl url)
    {
        var text = url.ToString();
        var hash = text.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            text = text[..hash];
        }

        var mark = text.IndexOf('?', StringComparison.Ordinal);
        return mark < 0 ? QueryCollection.Empty : QueryString.Parse(text[mark..]);
    }

    private static string? Value(QueryCollection query, string key) =>
        query.TryGetValue(key, out var value) ? value.ToString() : null;
}
