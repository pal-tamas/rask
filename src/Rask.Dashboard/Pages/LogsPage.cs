using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Core.Routing;
using Rask.Dashboard.Logging;
using Rask.Logging;

namespace Rask.Dashboard.Pages;

/// <summary>
/// The application's log, in two modes.
/// <para>
/// <b>Live</b> is the in-memory tail and is always available: the buffer raises an event when an entry
/// arrives, so the page renders on a real log line rather than on a timer, and reads no database at all. It
/// is bounded by count and gone on restart.
/// </para>
/// <para>
/// <b>History</b> appears when <c>Rask.Logging</c> is registered, and reads the durable store — paged, with a
/// text search. It polls, but against the log store's own SQLite file rather than the application database,
/// so it never competes with the processors for that write lock.
/// </para>
/// <para>
/// Two modes rather than one merged view because the writer flushes on an interval: the newest lines are in
/// the buffer but not yet on disk. A merged view would have to reconcile that seam on every render, and
/// would quietly disagree with itself for a second at a time.
/// </para>
/// </summary>
[Route("logs")]
[ParentRoute(typeof(DashboardLayout))]
public sealed partial class LogsPage(
    DashboardLogBuffer buffer,
    RaskDashboardOptions options,
    TimeProvider timeProvider,
    Navigator navigator,
    IServiceProvider services) : PollingPanel, IDisposable
{
    private readonly ILogs? _store = services.GetService<ILogs>();

    private bool _subscribed;
    private LogPage _history = LogPage.Empty(1, 1);
    private IReadOnlyList<string> _storedCategories = [];

    /// <summary>Which surface to read, from the query string so a shared link opens the same view.</summary>
    [QueryParam("view")]
    public string? View { get; set; }

    /// <summary>Minimum level to show, from the query string so a filtered view is a shareable link.</summary>
    [QueryParam("level")]
    public string? Level { get; set; }

    /// <summary>Category substring filter, likewise.</summary>
    [QueryParam("category")]
    public string? Category { get; set; }

    /// <summary>Free-text filter over the message and the exception. History only.</summary>
    [QueryParam("q")]
    public string? Query { get; set; }

    /// <summary>The 1-based page of the stored log. History only. Nullable so the factory keeps it optional.</summary>
    [QueryParam("page")]
    public int? Page { get; set; }

    /// <inheritdoc />
    protected override RaskDashboardOptions Options => options;

    /// <summary><c>true</c> when a durable store is registered, so History is offered at all.</summary>
    private bool HasStore => _store is not null;

    private bool IsHistory => HasStore && string.Equals(View, "history", StringComparison.OrdinalIgnoreCase);

    private int CurrentPage => Page is > 1 ? Page.Value : 1;

    private LogLevel? MinimumLevel =>
        Enum.TryParse<LogLevel>(Level, ignoreCase: true, out var parsed) ? parsed : null;

    /// <inheritdoc />
    public void Dispose()
    {
        Unsubscribe();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    protected override void OnUnmount() => Unsubscribe();

    /// <inheritdoc />
    protected override async Task<object?> LoadAsync(CancellationToken cancellationToken)
    {
        // Subscribed here rather than in OnMount because PollingPanel owns OnMountAsync: the live tail
        // still pushes, so a log line shows up immediately instead of on the next poll.
        if (!_subscribed)
        {
            buffer.Changed += OnLogged;
            _subscribed = true;
        }

        if (!IsHistory)
        {
            // Live mode reads memory only. Returning the buffer's shape keeps the poll loop's change
            // detection honest without touching a database — an idle app renders nothing.
            var entries = buffer.Snapshot(MinimumLevel, Category);
            return $"live:{entries.Count}:{(entries.Count > 0 ? entries[0].Sequence : 0)}";
        }

        var query = BuildQuery(Level, Category, Query, Page, options.PageSize);
        _history = await _store!.SearchAsync(query, cancellationToken).ConfigureAwait(false);

        // A page past the end — a bookmarked ?page= that retention has since trimmed — reads the last page there
        // is, rather than an empty table beside the stored count and a pager that cannot reach it.
        if (_history.Entries.Count == 0 && _history.TotalCount > 0 && _history.Page > _history.PageCount)
        {
            Page = _history.PageCount;
            _history = await _store
                .SearchAsync(BuildQuery(Level, Category, Query, Page, options.PageSize), cancellationToken)
                .ConfigureAwait(false);
        }
        _storedCategories = await _store.CategoriesAsync(cancellationToken).ConfigureAwait(false);

        // Total plus the ids on screen: a new entry changes the total, and paging changes the ids.
        return string.Join('|', [$"history:{_history.TotalCount}", .. _history.Entries.Select(e => e.Id)]);
    }

    /// <summary>
    /// Turns the query string into a store query. Pure and static so the mapping — which is where a
    /// filter silently going missing would actually happen — is testable without a rendered page.
    /// <para>
    /// Blank facets become <c>null</c> rather than empty strings: an empty <c>?q=</c> in a shared link
    /// must mean "no text filter", not "match the empty string".
    /// </para>
    /// </summary>
    internal static LogQuery BuildQuery(
        string? level,
        string? category,
        string? search,
        int? page,
        int pageSize) => new()
        {
            MinimumLevel = Enum.TryParse<LogLevel>(level, ignoreCase: true, out var parsed) ? parsed : null,
            Category = Blank(category),
            Search = Blank(search),
            Page = page is > 1 ? page.Value : 1,
            PageSize = pageSize,
        };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (!options.CaptureLogs && !HasStore)
        {
            return UiCard[
                UiEmpty
                    .Heading("Log capture is off")
                    .Detail("Set CaptureLogs = true on RaskDashboardOptions to keep a tail of recent entries, or add "
                    + "Rask.Logging to keep them across restarts.")
            ];
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        return [
            UiHeader.Heading("Logs").Caption(Caption()).Actions(HasStore ? ModeTabs() : null),
            DashboardError.Message(LoadError),
            IsHistory ? HistoryBody(now) : LiveBody(now),
            DashboardParked.Parked(IsParked).Resume(ResumeAsync),
        ];
    }

    // Deliberately says "at most" for the live tail: the dashboard's own floor is only half the story. The
    // logging pipeline applies the app's `Logging:LogLevel` rules FIRST, so an entry below those never
    // reaches this buffer however low LogMinimumLevel is set. Promising "Information and above" while
    // appsettings.Production.json says Warning is how an operator concludes the panel is broken when it is
    // working exactly as configured.
    private string Caption() => IsHistory
        ? $"{_history.TotalCount} stored entries, kept across restarts"
        : $"at most {options.LogBufferSize} entries, {options.LogMinimumLevel} and above, in memory only";

    private Component ModeTabs() =>
        UiTabs[
            ModeTab(null, "Live"),
            ModeTab("history", "History")
        ];

    private Component ModeTab(string? view, string label) =>
        UiTab
            .Key(label)
            .Href(Routes.LogsPage(View: view, Level: Level, Category: Category))
            .Label(label)
            .Active(IsHistory == (view is not null));

    // The grid's toolbar lays these out as one row from sm up and one control per line below it, which is
    // what three filters at 360px need: side by side, each is too narrow to show the value it is set to.
    private Component Filters() =>
        [
            UiTabs[
                LevelPill(null, "All"),
                LevelPill(LogLevel.Information, "Info+"),
                LevelPill(LogLevel.Warning, "Warning+"),
                LevelPill(LogLevel.Error, "Error+")
            ],
            CategoryFilter(),
            IsHistory ? SearchBox() : null
        ];

    private Component LevelPill(LogLevel? level, string label) =>
        UiTab
            .Key(label)
            .Href(Link(level: level?.ToString(), category: Category))
            .Label(label)
            .Active(MinimumLevel == level);

    // A native select rather than a drawn list: a real application has dozens of logger categories, and the
    // platform's own picker is keyboard-navigable, needs no script and is the right control on a phone. Only
    // the categories actually present are offered.
    private Component? CategoryFilter()
    {
        var categories = IsHistory ? _storedCategories : buffer.Categories();
        if (categories.Count == 0)
        {
            return null;
        }

        IReadOnlyList<(string Value, string Text)> choices = [("", "All categories"), .. categories.Select(c => (c, c))];

        return UiSelect
            .Value(Category ?? "")
            .Options(choices)
            .Label("Category")
            .Native(true)
            .OnChange(CategoryChangedAsync);
    }

    private Task CategoryChangedAsync(string value)
    {
        navigator.NavigateTo(Link(level: Level, category: string.IsNullOrEmpty(value) ? null : value));
        return Task.CompletedTask;
    }

    private Component SearchBox() =>
        UiSearch
            .Placeholder("Search message or exception")
            .AccessibleLabel("Search stored log entries")
            .Value(Query)
            .OnSearch(SearchAsync);

    private Task SearchAsync(string value)
    {
        // Navigate, do not merely reload (#936). The property is declared [QueryParam("q")] and the page
        // calls the result a shareable link, but assigning it and re-rendering left the address bar on
        // the previous URL — so the link in the bar answered a different question than the page on
        // screen, and copying it lost the search. CategoryChangedAsync beside this was already right;
        // this is the same shape.
        Query = string.IsNullOrWhiteSpace(value) ? null : value;
        Page = 1;
        navigator.NavigateTo(Link(level: Level, category: Category, query: Query));
        return Task.CompletedTask;
    }

    /// <summary>
    /// The current view's URL with the given facets. Every filter is carried explicitly so that changing one
    /// composes with the others instead of silently resetting them.
    /// </summary>
    private string Link(string? level, string? category, int? page = null, string? query = null) =>
        Routes.LogsPage(
            View: IsHistory ? "history" : null,
            Level: level,
            Category: category,
            // `query` defaults to null, which for every OTHER caller has to mean "keep what is there"
            // rather than "clear it" — hence the fallback. The search box passes its own value, which is
            // how clearing the box reaches the URL as an absent q rather than as the previous term.
            Query: IsHistory ? query ?? Query : null,
            Page: page is > 1 ? page : null);

    private Component LiveBody(DateTime now) =>
        LogGrid(
            [.. buffer.Snapshot(MinimumLevel, Category).Select(ToRow)],
            now,
            UiEmpty
                .Heading("Nothing captured yet")
                .Detail("Entries appear here as the application logs them — subject to the app's own "
                + "Logging:LogLevel configuration, which filters before the dashboard sees them."),
            paged: false);

    private Component HistoryBody(DateTime now) =>
        LogGrid(
            [.. _history.Entries.Select(ToRow)],
            now,
            IsLoading
                ? DashboardLoading
                : UiEmpty
                    .Heading("Nothing stored matches")
                    .Detail("Either nothing has been logged into the store yet, or no entry matches this filter. "
                    + "Retention drops entries by age and by count."),
            paged: true);

    // One grid for both surfaces, so the two modes cannot drift into rendering an entry differently.
    //
    // History's pages are LINKS: paging is navigation, so it stays shareable, back-navigable, and needs no
    // round trip to the server to decide where it goes. The grid counts pages from zero and the address from
    // one, and the conversion happens here, once.
    private Component LogGrid(IReadOnlyList<LogRow> rows, DateTime now, Component empty, bool paged)
    {
        var grid = UiDataGrid.Data(rows)
            .RowKey(r => r.Key)
            .Label(paged ? "Stored log entries" : "Recent log entries")
            .Toolbar(Filters())
            .Loading(paged && IsLoading)
            .Empty(empty);

        if (paged)
        {
            grid = grid
                // The store's page size, not the option: the store caps it, and a pager counting in the option
                // would offer pages past the rows the store can hand back.
                .PageSize(_history.PageSize)
                .Page(CurrentPage - 1)
                .TotalCount((int)Math.Min(_history.TotalCount, int.MaxValue))
                .PageHref(page => Link(Level, Category, page + 1));
        }

        // Every column at every table width. The category used to fold under the message on a narrow screen, so
        // hiding it between sm and lg would lose a fact the old table kept; on a phone each column becomes its own
        // labelled line.
        return grid[c => [
            c.Field(r => r.Timestamp).Title("When").Cell(r =>
                Span.Title(r.Timestamp.UtcDateTime.ToString("u"))[DashboardParts.Ago(r.Timestamp.UtcDateTime, now)]),
            c.Field(r => r.Level).Title("Level").Cell(r => LevelBadge(r.Level)),
            c.Field(r => r.Category).Title("Category").Mono(true),
            c.Field(r => r.Message).Title("Message").Cell(MessageCell),
        ]];
    }

    // The stack trace is the reason an error entry is worth surfacing at all, but it would drown the table,
    // so it renders muted beneath the message rather than in a separate view.
    private static Component MessageCell(LogRow row) =>
        Div[
            Div[row.Message],
            row.Exception is { } ex ? UiCode.Content(ex) : null,
            ScopeChips(row.Scopes)
        ];

    // One row shape for both surfaces. The live tail has no scopes: it is the in-memory ring buffer, which
    // predates the store and captures only what it is handed. History reads them from the stored row.
    private static LogRow ToRow(DashboardLogEntry entry) => new(
        entry.Sequence, entry.Timestamp, entry.Level, entry.Category, entry.Message, entry.Exception, null);

    private static LogRow ToRow(LogRecord record) => new(
        record.Id, record.Timestamp, record.Level, record.Category, record.Message, record.Exception,
        record.Scopes);

    // The ambient state the entry was written under — the request id, the user id. This is what turns one
    // line into a thread you can pull. Mono badges wrap, because a scope value is a request id and one
    // unbreakable 40-character token is enough to push the whole table wider than a phone; a space between
    // them is the gap, the same as between any two inline things.
    private static Component? ScopeChips(IReadOnlyList<LogScopeValue>? scopes) =>
        scopes is null || scopes.Count == 0
            ? null
            : Div[
                scopes.SelectMany(s => new Component[]
                {
                    UiBadge.Key(s.Key).Mono(true)[$"{s.Key}={s.Value}"],
                    " ",
                })
            ];

    private static Component LevelBadge(LogLevel level) => UiBadge
        .Tone(level switch
        {
            LogLevel.Critical or LogLevel.Error => UiTone.Error,
            LogLevel.Warning => UiTone.Warning,
            LogLevel.Information => UiTone.Info,
            _ => null,
        })[level.ToString()];

    private void OnLogged()
    {
        // Only the live tail is push-driven. In History mode a render per log line would mean a database
        // query per log line — a self-inflicted storm exactly when the app is at its noisiest.
        if (!IsHistory)
        {
            StateHasChanged();
        }
    }

    private void Unsubscribe()
    {
        if (_subscribed)
        {
            buffer.Changed -= OnLogged;
            _subscribed = false;
        }
    }

    /// <summary>One row on screen, from either surface.</summary>
    private sealed record LogRow(
        long Key,
        DateTimeOffset Timestamp,
        LogLevel Level,
        string Category,
        string Message,
        string? Exception,
        IReadOnlyList<LogScopeValue>? Scopes);
}
