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
            return DashboardEmpty.Heading("Log capture is off")
                .Detail("Set CaptureLogs = true on RaskDashboardOptions to keep a tail of recent entries, or add "
                + "Rask.Logging to keep them across restarts.");
        }

        return [
            OpsHeader.Heading("Logs").Caption(Caption()).Actions(HasStore ? ModeTabs() : null),
            DashboardError.Message(LoadError),
            Filters(),
            IsHistory ? HistoryBody() : LiveBody(),
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
        OpsTabs[
            ModeTab(null, "Live"),
            ModeTab("history", "History")
        ];

    private Component ModeTab(string? view, string label) =>
        OpsTab
            .Key(label)
            .Href(Routes.LogsPage(View: view, Level: Level, Category: Category))
            .Label(label)
            .Active(IsHistory == (view is not null));

    // Stacked on a phone, one row from sm up. Three filter controls side by side at 360px leaves each of
    // them too narrow to read the value it is set to, which is the only thing a filter has to show.
    private Component Filters() =>
        Div.Class("mb-4 flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center")[
            OpsTabs[
                LevelPill(null, "All"),
                LevelPill(LogLevel.Information, "Info+"),
                LevelPill(LogLevel.Warning, "Warning+"),
                LevelPill(LogLevel.Error, "Error+")
            ],
            CategoryFilter(),
            IsHistory ? SearchBox() : null
        ];

    private Component LevelPill(LogLevel? level, string label) =>
        OpsTab
            .Key(label)
            .Href(Link(level: level?.ToString(), category: Category))
            .Label(label)
            .Active(MinimumLevel == level);

    // A native select rather than a menu: a real application has dozens of logger categories, and this
    // needs no JavaScript, is keyboard-navigable and gets the platform's own picker on a phone. Only the
    // categories actually present are offered.
    private Component? CategoryFilter()
    {
        var categories = IsHistory ? _storedCategories : buffer.Categories();
        if (categories.Count == 0)
        {
            return null;
        }

        // One sequence: the children indexer takes individual components or an enumerable, not both.
        var options = new List<Component?> { Option.Value("")["All categories"] };
        options.AddRange(categories.Select(c =>
            Option.Key(c).Value(c).Selected(string.Equals(c, Category, StringComparison.Ordinal))[c]));

        return Select
            .Value(Category ?? "")
            .Class(
                "min-h-11 w-full rounded-lg border border-ui-line bg-ui-bg px-2.5 py-1.5 text-sm text-ui-ink "
                + "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ui-brand "
                + "sm:min-h-0 sm:w-auto")
            .Aria(new Dictionary<string, string?> { ["label"] = "Filter by category" })
            .OnChangeAsync(CategoryChangedAsync)[options];
    }

    private Task CategoryChangedAsync(string value)
    {
        navigator.NavigateTo(Link(level: Level, category: string.IsNullOrEmpty(value) ? null : value));
        return Task.CompletedTask;
    }

    private Component SearchBox() =>
        UiSearch
            .Placeholder("Search message or exception")
            .Label("Search stored log entries")
            .Value(Query)
            .OnSearch(SearchAsync);

    private async Task SearchAsync(string value)
    {
        Query = string.IsNullOrWhiteSpace(value) ? null : value;
        Page = 1;
        await LoadAsync(CancellationToken).ConfigureAwait(false);
        StateHasChanged();
    }

    /// <summary>
    /// The current view's URL with the given facets. Every filter is carried explicitly so that changing one
    /// composes with the others instead of silently resetting them.
    /// </summary>
    private string Link(string? level, string? category, int? page = null) =>
        Routes.LogsPage(
            View: IsHistory ? "history" : null,
            Level: level,
            Category: category,
            Query: IsHistory ? Query : null,
            Page: page is > 1 ? page : null);

    private Component LiveBody()
    {
        var entries = buffer.Snapshot(MinimumLevel, Category);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return entries.Count == 0
            ? DashboardEmpty.Heading("Nothing captured yet")
                .Detail("Entries appear here as the application logs them — subject to the app's own "
                + "Logging:LogLevel configuration, which filters before the dashboard sees them.")
            : LogTable(entries.Select(ToRow), now);
    }

    private Component HistoryBody()
    {
        if (IsLoading)
        {
            return DashboardLoading;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        return _history.Entries.Count == 0
            ? DashboardEmpty.Heading("Nothing stored matches")
                .Detail("Either nothing has been logged into the store yet, or no entry matches this filter. "
                + "Retention drops entries by age and by count.")
            : [LogTable(_history.Entries.Select(ToRow), now), Pager()];
    }

    private Component? Pager()
    {
        if (_history.PageCount <= 1)
        {
            return null;
        }

        var page = CurrentPage;

        // justify-between rather than a centred group: on a phone this puts the two controls at the edges,
        // which is where thumbs are.
        return Div.Class("mt-4 flex items-center justify-between gap-3")[
            PagerLink("Previous", page - 1, page <= 1),
            Span.Class("text-center text-xs text-ui-muted")[
                Span[$"Page {page} of {_history.PageCount}"],
                Span.Class("hidden sm:inline")[$" — {_history.TotalCount} entries"]
            ],
            PagerLink("Next", page + 1, page >= _history.PageCount)
        ];
    }

    // A link rather than a button: paging is navigation, so it stays shareable, back-navigable, and needs
    // no round trip to the server to decide where it goes. A disabled end is rendered as plain text, since
    // a link that goes nowhere should not be focusable.
    private Component PagerLink(string label, int page, bool disabled) =>
        disabled
            ? Span.Class($"{Ops.Button} pointer-events-none opacity-40")[label]
            : NavLink.Href(Link(Level, Category, page)).Class($"{Ops.Button} no-underline")[label];

    // One row shape for both surfaces, so the two modes cannot drift into rendering an entry differently.
    // The live tail has no scopes: it is the in-memory ring buffer, which predates the store and captures
    // only what it is handed. History reads them from the stored row.
    private static LogRow ToRow(DashboardLogEntry entry) => new(
        entry.Sequence, entry.Timestamp, entry.Level, entry.Category, entry.Message, entry.Exception, null);

    private static LogRow ToRow(LogRecord record) => new(
        record.Id, record.Timestamp, record.Level, record.Category, record.Message, record.Exception,
        record.Scopes);

    private static Component LogTable(IEnumerable<LogRow> rows, DateTime now) =>
        OpsTable[
            // The message is the column an operator came for, so it is the one that survives a narrow
            // screen; when, level and category fold in above it rather than scrolling off to the right.
            Thead.Class("border-b border-ui-line text-xs text-ui-muted")[
                Tr[
                    Th.Class("hidden px-3 py-2 font-medium sm:table-cell")["When"],
                    Th.Class("hidden px-3 py-2 font-medium sm:table-cell")["Level"],
                    Th.Class("hidden px-3 py-2 font-medium lg:table-cell")["Category"],
                    Th.Class("px-3 py-2 font-medium")["Message"]
                ]
            ],
            Tbody[rows.Select(r => Tr.Key(r.Key).Class("border-b border-ui-line/60 last:border-0")[
                Td.Class("hidden whitespace-nowrap px-3 py-2 align-top text-xs text-ui-muted sm:table-cell")
                    .Title(r.Timestamp.UtcDateTime.ToString("u"))[
                    DashboardParts.Ago(r.Timestamp.UtcDateTime, now)
                ],
                Td.Class("hidden px-3 py-2 align-top sm:table-cell")[LevelBadge(r.Level)],
                Td.Class($"hidden px-3 py-2 align-top lg:table-cell {Ops.Mono} text-ui-muted")[r.Category],
                Td.Class("w-full max-w-0 px-3 py-2 align-top")[
                    Div.Class("mb-1 flex flex-wrap items-center gap-x-2 gap-y-1 sm:hidden")[
                        LevelBadge(r.Level),
                        Span.Class("text-xs text-ui-muted").Title(r.Timestamp.UtcDateTime.ToString("u"))[
                            DashboardParts.Ago(r.Timestamp.UtcDateTime, now)
                        ]
                    ],
                    Div.Class("break-words text-ui-ink")[r.Message],
                    // The category is worth keeping on a phone, just not in a column of its own.
                    Div.Class($"mt-0.5 break-all text-ui-muted lg:hidden {Ops.Mono}")[r.Category],
                    // The stack trace is the reason an error entry is worth surfacing at all, but it would
                    // drown the table, so it renders muted beneath rather than in a separate view.
                    r.Exception is { } ex
                        ? Pre.Class(
                            $"mt-1 max-h-60 overflow-auto whitespace-pre-wrap break-all {Ops.Mono} text-ui-muted")[
                            ex
                        ]
                        : null,
                    // The ambient state the entry was written under — the request id, the user id. This is
                    // what turns one line into a thread you can pull: copy a value into the scope filter
                    // and the page shows everything else that happened on the same request.
                    ScopeChips(r.Scopes)
                ]
            ])]
        ];

    private static Component? ScopeChips(IReadOnlyList<LogScopeValue>? scopes) =>
        scopes is null || scopes.Count == 0
            ? null
            : Div.Class("mt-1.5 flex flex-wrap gap-1")[
                // break-all, because a scope value is a request id: one unbreakable 40-character token is
                // enough to push the whole table wider than a phone.
                scopes.Select(s => OpsBadge
                    .Key(s.Key)
                    .Label($"{s.Key}={s.Value}")
                    .Class($"max-w-full break-all {Ops.Mono}"))
            ];

    private static Component LevelBadge(LogLevel level) => OpsBadge
        .Label(level.ToString())
        .Tone(level switch
        {
            LogLevel.Critical or LogLevel.Error => "danger",
            LogLevel.Warning => "warn",
            LogLevel.Information => "info",
            _ => null,
        });

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
