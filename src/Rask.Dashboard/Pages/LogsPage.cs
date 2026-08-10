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
    IServiceProvider services) : PollingPanel, IDisposable
{
    private readonly ILogStore? _store = services.GetService<ILogStore>();

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
        _history = await _store!.QueryAsync(query, cancellationToken).ConfigureAwait(false);
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
            Div(Class: "d-flex align-items-center gap-3 mb-3")[
                H1(Class: "h4 mb-0")["Logs"],
                Span(Class: "text-body-secondary small")[Caption()],
                HasStore ? Div(Class: "ms-auto")[ModeTabs()] : null
            ],
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
    private new string Caption() => IsHistory
        ? $"{_history.TotalCount} stored entries, kept across restarts"
        : $"at most {options.LogBufferSize} entries, {options.LogMinimumLevel} and above, in memory only";

    private Component ModeTabs() =>
        BsNav(Class: "nav-pills gap-1")[
            ModeTab(null, "Live"),
            ModeTab("history", "History")
        ];

    private Component ModeTab(string? view, string label) =>
        BsNavItem(Key: label)[
            BsLink(
                Routes.LogsPage(View: view, Level: Level, Category: Category),
                Class: Bs.Join("nav-link", IsHistory == (view is not null) ? "active" : null))[label]
        ];

    private Component Filters() =>
        Div(Class: "d-flex flex-wrap align-items-center gap-2 mb-3")[
            BsNav(Class: "nav-pills gap-1")[
                LevelPill(null, "All"),
                LevelPill(LogLevel.Information, "Info+"),
                LevelPill(LogLevel.Warning, "Warning+"),
                LevelPill(LogLevel.Error, "Error+")
            ],
            CategoryFilter(),
            IsHistory ? SearchBox() : null
        ];

    private Component LevelPill(LogLevel? level, string label) =>
        BsNavItem(Key: label)[
            BsLink(
                Link(level: level?.ToString(), category: Category),
                Class: Bs.Join("nav-link", MinimumLevel == level ? "active" : null))[label]
        ];

    // A dropdown rather than a pill row: a real application has dozens of logger categories, and only the
    // ones actually present are worth offering.
    private Component? CategoryFilter()
    {
        var categories = IsHistory ? _storedCategories : buffer.Categories();
        if (categories.Count == 0)
        {
            return null;
        }

        return BsDropdown(
            Label: Category is { Length: > 0 } ? Category : "All categories",
            Color: BsColor.Secondary,
            Outline: true,
            Size: BsSize.Sm)[CategoryItems(categories)];
    }

    // One sequence rather than a mixed argument list: the children indexer takes either individual
    // components or an enumerable, not both.
    private IEnumerable<Component> CategoryItems(IReadOnlyList<string> categories)
    {
        yield return BsDropdownItem(
            Key: "__all",
            Href: Link(level: Level, category: null),
            Active: string.IsNullOrEmpty(Category))["All categories"];

        yield return BsDropdownItem(Key: "__divider", Divider: true);

        foreach (var category in categories)
        {
            yield return BsDropdownItem(
                Key: category,
                Href: Link(level: Level, category: category),
                Active: string.Equals(category, Category, StringComparison.Ordinal))[category];
        }
    }

    private Component SearchBox() =>
        Rask.Core.Components.Generated.Input<string>(
            Type: InputType.Search,
            Class: "form-control form-control-sm",
            Style: "max-width:18rem",
            Placeholder: "Search message or exception",
            Value: Query,
            OnChangeAsync: SearchAsync);

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
    private new string Link(string? level, string? category, int? page = null) =>
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
            : Table(entries.Select(ToRow), now);
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
            : [Table(_history.Entries.Select(ToRow), now), Pager()];
    }

    private Component? Pager()
    {
        if (_history.PageCount <= 1)
        {
            return null;
        }

        var page = CurrentPage;
        return Div(Class: "d-flex align-items-center gap-2")[
            PagerLink("Previous", page - 1, page <= 1),
            Span(Class: "small text-body-secondary")[
                $"Page {page} of {_history.PageCount} — {_history.TotalCount} entries"
            ],
            PagerLink("Next", page + 1, page >= _history.PageCount)
        ];
    }

    // A link rather than a button: paging is navigation, so it stays shareable, back-navigable, and needs
    // no round trip to the server to decide where it goes.
    private Component PagerLink(string label, int page, bool disabled) =>
        BsLink(
            disabled ? "#" : Link(Level, Category, page),
            Class: Bs.Join("btn btn-outline-secondary btn-sm", disabled ? "disabled" : null),
            Aria: disabled ? new Dictionary<string, string?> { ["disabled"] = "true" } : null)[label];

    // One row shape for both surfaces, so the two modes cannot drift into rendering an entry differently.
    // The live tail has no scopes: it is the in-memory ring buffer, which predates the store and captures
    // only what it is handed. History reads them from the stored row.
    private static LogRow ToRow(DashboardLogEntry entry) => new(
        entry.Sequence, entry.Timestamp, entry.Level, entry.Category, entry.Message, entry.Exception, null);

    private static LogRow ToRow(LogRecord record) => new(
        record.Id, record.Timestamp, record.Level, record.Category, record.Message, record.Exception,
        record.Scopes);

    private static new Component Table(IEnumerable<LogRow> rows, DateTime now) =>
        BsTable(Small: true, Hover: true, Responsive: true)[
            Thead()[Tr()[Th()["When"], Th()["Level"], Th()["Category"], Th()["Message"]]],
            Tbody()[rows.Select(r => Tr(Key: r.Key)[
                Td(Class: "text-nowrap text-body-secondary", Title: r.Timestamp.UtcDateTime.ToString("u"))[
                    DashboardParts.Ago(r.Timestamp.UtcDateTime, now)
                ],
                Td()[LevelBadge(r.Level)],
                Td(Class: "font-monospace small text-body-secondary")[r.Category],
                Td()[
                    Div()[r.Message],
                    // The stack trace is the reason an error entry is worth surfacing at all, but it would
                    // drown the table, so it renders muted beneath rather than in a separate view.
                    r.Exception is { } ex
                        ? Pre(Class: "small text-body-secondary text-wrap mb-0 mt-1")[ex]
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
            : Div(Class: "d-flex flex-wrap gap-1 mt-1")[
                scopes.Select(s =>
                    BsBadge(Color: BsColor.Secondary, Class: "fw-normal font-monospace", Key: s.Key)[
                        $"{s.Key}={s.Value}"
                    ])
            ];

    private static Component LevelBadge(LogLevel level) => BsBadge(Color: level switch
    {
        LogLevel.Critical or LogLevel.Error => BsColor.Danger,
        LogLevel.Warning => BsColor.Warning,
        LogLevel.Information => BsColor.Info,
        _ => BsColor.Secondary,
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
