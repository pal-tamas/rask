using System.Text.RegularExpressions;
using Rask.Cqrs;
using Rask.Querying;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.Table.Of(query)</c>: skeleton rows while the query first loads, the query's own rows once it has
///     answered, and the empty row when it found nothing — the headings throughout.
/// </summary>
/// <remarks>
///     What the browser does at a later press (the rows on screen turn into skeleton rows until the answer is on the
///     page) is <c>rask-skeleton.ts</c>'s, measured frame by frame in <c>TableSkeletonTests</c> (Server E2E).
/// </remarks>
public sealed partial class UiTableOfQueryTests : global::Rask.Core.RaskMarkup
{
    private static readonly string[] Names = ["Lindsey", "Hanna", "Kianna"];

    [Fact]
    public void A_first_load_shows_the_headings_and_a_skeleton_row_per_row_of_the_page()
    {
        var (query, _) = Pending();

        var html = Invoices(query, total: 40).ToHtml();

        Assert.Contains("data-rask-skeleton=\"loading\"", html, StringComparison.Ordinal);
        Assert.Contains("<thead data-ui-columns>", html, StringComparison.Ordinal);
        Assert.Equal(5, Rows(html));
        Assert.Equal(5 * 3, Regex.Count(html, "data-ui-skeleton-line"));
    }

    [Fact]
    public void A_first_load_on_the_last_page_shows_only_the_rows_left()
    {
        var (query, _) = Pending();

        var html = Invoices(query, total: 12, page: 3).ToHtml();

        Assert.Equal(2, Rows(html));
    }

    [Fact]
    public async Task An_answered_query_shows_its_own_rows_and_says_it_is_not_loading()
    {
        var (query, answer) = Pending();
        answer.SetResult(Names);
        await Settle(query);

        var html = Invoices(query, total: 40).ToHtml();

        Assert.Contains("data-rask-skeleton=\"\"", html, StringComparison.Ordinal);
        Assert.Equal(3, Rows(html));
        Assert.DoesNotContain("data-ui-skeleton-line", html, StringComparison.Ordinal);
        Assert.Contains(">Hanna</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_query_that_found_nothing_says_so_in_one_row_across_every_column()
    {
        var (query, answer) = Pending();
        answer.SetResult([]);
        await Settle(query);

        var html = Invoices(query, total: 0).ToHtml();

        Assert.Contains("<thead data-ui-columns>", html, StringComparison.Ordinal);
        Assert.Equal(1, Rows(html));
        Assert.Matches("<td [^>]*colspan=\"3\"[^>]*>No results.</td>", html);
    }

    [Fact]
    public async Task A_later_load_that_keeps_nothing_on_screen_shows_as_many_skeleton_rows_as_there_were_rows()
    {
        var page = 1;
        var answers = new Dictionary<int, TaskCompletionSource<string[]>>
        {
            [1] = new(), [2] = new(),
        };
        using var query = Client().Query(QueryKey.Of("invoices"), () => page, (asked, _) => answers[asked].Task);
        _ = query.Data;
        answers[1].SetResult(Names);
        await Settle(query);
        _ = Invoices(query, total: 40).ToHtml();
        page = 2;
        _ = query.Data;

        var html = Invoices(query, total: 40).ToHtml();

        Assert.Contains("data-rask-skeleton=\"loading\"", html, StringComparison.Ordinal);
        Assert.Equal(3, Rows(html));
        Assert.Equal(3 * 3, Regex.Count(html, "data-ui-skeleton-line"));
    }

    [Fact]
    public async Task A_later_load_that_keeps_the_previous_rows_shows_them_and_says_it_is_loading()
    {
        var page = 1;
        var answers = new Dictionary<int, TaskCompletionSource<string[]>>
        {
            [1] = new(), [2] = new(),
        };
        using var query = Client().Query(
            QueryKey.Of("invoices"), () => page, (asked, _) => answers[asked].Task, new QueryOptions { KeepPreviousData = true });
        _ = query.Data;
        answers[1].SetResult(Names);
        await Settle(query);
        _ = Invoices(query, total: 40).ToHtml();
        page = 2;
        _ = query.Data;

        var html = Invoices(query, total: 40).ToHtml();

        Assert.Contains("data-rask-skeleton=\"loading\"", html, StringComparison.Ordinal);
        Assert.Contains(">Kianna</td>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-skeleton-line", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_without_a_query_carries_no_skeleton_mark()
    {
        var html = Ui.Table[Ui.TableRows[Ui.TableRow[Ui.TableCell["Lindsey"]]]].ToHtml();

        Assert.DoesNotContain("data-rask-skeleton", html, StringComparison.Ordinal);
    }

    private static global::Rask.Core.Component Invoices(Query<string[]> query, int total, int page = 1) =>
        Ui.Table.Of(query).Paginate(Ui.Pagination.Paginator(new UiPaginator { Page = page, PerPage = 5, Total = total }))[
            Ui.TableColumns[Ui.TableColumn["Customer"], Ui.TableColumn["Date"], Ui.TableColumn["Amount"]],
            Ui.TableRows[
                (query.Data ?? []).Select(name => Ui.TableRow.Key(name)[Ui.TableCell[name], Ui.TableCell["Today"], Ui.TableCell["$1"]])
            ]
        ];

    private static int Rows(string html) => Regex.Count(html[html.IndexOf("<tbody", StringComparison.Ordinal)..], "<tr[ >]");

    private static (Query<string[]> Query, TaskCompletionSource<string[]> Answer) Pending()
    {
        var answer = new TaskCompletionSource<string[]>();
        var query = Client().Query(QueryKey.Of("invoices"), _ => answer.Task);
        _ = query.Data;
        return (query, answer);
    }

    private static async Task Settle<T>(Query<T> query)
    {
        _ = query.Data;
        if (query.Entry.InFlight is { } running)
        {
            await running;
        }
    }

    private static SessionQueryClient Client() => new(new NothingToAsk());

    // A query cache needs a dispatcher. These queries fetch by their own lambdas.
    private sealed class NothingToAsk : IDispatcher
    {
        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send(ICommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish<TEvent>(TEvent e, CancellationToken cancellationToken = default)
            where TEvent : IEvent => throw new NotSupportedException();

        public IAsyncEnumerable<TEvent> Subscribe<TEvent>(CancellationToken cancellationToken = default)
            where TEvent : IEvent => throw new NotSupportedException();

        public IAsyncEnumerable<TEvent> Subscribe<TEvent>(
            ISubscription<TEvent> subscription, CancellationToken cancellationToken = default)
            where TEvent : IEvent => throw new NotSupportedException();
    }
}
