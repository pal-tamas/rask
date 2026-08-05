using Microsoft.Extensions.Logging;
using Rask.Core.Routing;
using Rask.Dashboard.Logging;

namespace Rask.Dashboard.Pages;

/// <summary>
/// The recent log tail. Unlike the other panels this one doesn't poll: the buffer raises an event when an
/// entry arrives, so the page renders on a real log line rather than on a timer. Nothing is read from the
/// database, so there is no write-lock cost either.
/// </summary>
[Route("logs")]
[ParentRoute(typeof(DashboardLayout))]
public sealed class LogsPage(
    DashboardLogBuffer buffer,
    RaskDashboardOptions options,
    TimeProvider timeProvider) : Component, IDisposable
{
    private bool _subscribed;

    /// <summary>Minimum level to show, from the query string so a filtered view is a shareable link.</summary>
    [QueryParam("level")]
    public string? Level { get; set; }

    /// <summary>Category substring filter, likewise.</summary>
    [QueryParam("category")]
    public string? Category { get; set; }

    private LogLevel? MinimumLevel =>
        Enum.TryParse<LogLevel>(Level, ignoreCase: true, out var parsed) ? parsed : null;

    protected override void OnMount()
    {
        buffer.Changed += OnLogged;
        _subscribed = true;
    }

    protected override void OnUnmount() => Unsubscribe();

    /// <inheritdoc />
    public void Dispose()
    {
        Unsubscribe();
        GC.SuppressFinalize(this);
    }

    protected override Component? Render()
    {
        if (!options.CaptureLogs)
        {
            return DashboardParts.Empty(
                "Log capture is off",
                "Set CaptureLogs = true on RaskDashboardOptions to keep a tail of recent entries.");
        }

        var entries = buffer.Snapshot(MinimumLevel, Category);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return [
            Div(Class: "d-flex align-items-center gap-3 mb-3")[
                H1(Class: "h4 mb-0")["Logs"],
                // Deliberately says "at most": the dashboard's own floor is only half the story. The
                // logging pipeline applies the app's `Logging:LogLevel` rules FIRST, so an entry below
                // those never reaches this buffer however low LogMinimumLevel is set. Promising
                // "Information and above" while appsettings.Production.json says Warning is how an
                // operator concludes the panel is broken when it is working exactly as configured.
                Span(Class: "text-body-secondary small")[
                    $"at most {options.LogBufferSize} entries, {options.LogMinimumLevel} and above, in memory only"
                ]
            ],
            Filters(),
            entries.Count == 0
                ? DashboardParts.Empty(
                    "Nothing captured yet",
                    "Entries appear here as the application logs them — subject to the app's own "
                    + "Logging:LogLevel configuration, which filters before the dashboard sees them.")
                : Table(entries, now),
        ];
    }

    private Component Filters() =>
        Div(Class: "d-flex flex-wrap align-items-center gap-2 mb-3")[
            BsNav(Class: "nav-pills gap-1")[
                LevelPill(null, "All"),
                LevelPill(LogLevel.Information, "Info+"),
                LevelPill(LogLevel.Warning, "Warning+"),
                LevelPill(LogLevel.Error, "Error+")
            ],
            CategoryFilter()
        ];

    private Component LevelPill(LogLevel? level, string label) =>
        BsNavItem(Key: label)[
            BsLink(
                Routes.LogsPage(Level: level?.ToString(), Category: Category),
                Class: Bs.Join("nav-link", MinimumLevel == level ? "active" : null))[label]
        ];

    // A dropdown rather than a pill row: a real application has dozens of logger categories, and only the
    // ones currently in the buffer are worth offering.
    private Component? CategoryFilter()
    {
        var categories = buffer.Categories();
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
            Href: Routes.LogsPage(Level: Level),
            Active: string.IsNullOrEmpty(Category))["All categories"];

        yield return BsDropdownItem(Key: "__divider", Divider: true);

        foreach (var category in categories)
        {
            yield return BsDropdownItem(
                Key: category,
                Href: Routes.LogsPage(Level: Level, Category: category),
                Active: string.Equals(category, Category, StringComparison.Ordinal))[category];
        }
    }

    private static Component Table(IReadOnlyList<DashboardLogEntry> entries, DateTime now) =>
        BsTable(Small: true, Hover: true, Responsive: true)[
            Thead()[Tr()[Th()["When"], Th()["Level"], Th()["Category"], Th()["Message"]]],
            Tbody()[entries.Select(e => Tr(Key: e.Sequence)[
                Td(Class: "text-nowrap text-body-secondary")[DashboardParts.Ago(e.Timestamp.UtcDateTime, now)],
                Td()[LevelBadge(e.Level)],
                Td(Class: "font-monospace small text-body-secondary")[e.Category],
                Td()[
                    Div()[e.Message],
                    // The stack trace is the reason an error entry is worth surfacing at all, but it would
                    // drown the table, so it renders muted beneath rather than in a separate view.
                    e.Exception is { } ex
                        ? Pre(Class: "small text-body-secondary text-wrap mb-0 mt-1")[ex]
                        : null
                ]
            ])]
        ];

    private static Component LevelBadge(LogLevel level) => BsBadge(Color: level switch
    {
        LogLevel.Critical or LogLevel.Error => BsColor.Danger,
        LogLevel.Warning => BsColor.Warning,
        LogLevel.Information => BsColor.Info,
        _ => BsColor.Secondary,
    })[level.ToString()];

    private void OnLogged() => StateHasChanged();

    private void Unsubscribe()
    {
        if (_subscribed)
        {
            buffer.Changed -= OnLogged;
            _subscribed = false;
        }
    }
}
