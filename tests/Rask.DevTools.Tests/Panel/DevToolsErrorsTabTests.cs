using Rask.Core;
using Rask.DevTools.Panel;
using Rask.DevTools.Probe;
using Rask.Testing;

namespace Rask.DevTools.Tests.Panel;

/// <summary>
///     The Errors tab and the tab strip over logs filled by hand: what a row says, the filters, "Show in tree", and the
///     unseen count that clears once the tab has been looked at.
/// </summary>
public sealed class DevToolsErrorsTabTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    private static (DevToolsErrorLog Page, DevToolsErrorLog App) Logs()
    {
        var page = new DevToolsErrorLog();
        var app = new DevToolsErrorLog();
        var entry = page.Record(DevToolsErrorKind.Handler, false, "InvalidOperationException", "the save failed",
            "at Shop.Save()", "Row", 42, caught: true, appWide: false, At.AddSeconds(2));
        page.Enclosing(entry, "Board");
        app.Record(DevToolsErrorKind.Diagnostic, true, "Rask.Diff", "two siblings share a key", null, null, null, false, true,
            At.AddSeconds(1));
        return (page, app);
    }

#pragma warning disable RASK014 // the tab and the strip rendered alone, the way the panel page would chain them
    private static RenderedComponent Tab(DevToolsErrorLog page, DevToolsErrorLog app, Action<long>? show = null) =>
        Test.Render(new DevToolsErrorsTab
        {
            PageErrors = page,
            AppErrors = app,
            OnShowInTree = show is null ? null : new Callback<long>(show),
            ReportEnvironment = new DevToolsBugReport.Environment("Server", "0.22.0", ".NET 10", "macOS", "Chrome 131"),
        });

    private static RenderedComponent Tabs(string current, DevToolsErrorLog page, DevToolsErrorLog app, Action<string>? select = null) =>
        Test.Render(new DevToolsTabs
        {
            Current = current,
            PageErrors = page,
            AppErrors = app,
            OnSelect = select is null ? null : new Callback<string>(select),
        });
#pragma warning restore RASK014

    private static Task<string> Click(RenderedComponent page, string text) =>
        page.InvokeAsync(
            page.FindAll("button").First(b => b.TextContent.Trim() == text).Attributes["data-rask-on-click"],
            "{\"type\":\"click\"}");

    [Fact]
    public void A_row_says_what_failed_where_whether_it_was_caught_and_when()
    {
        var (page, app) = Logs();
        var tab = Tab(page, app);

        var rows = tab.FindAll(".card-body").Select(r => r.TextContent).ToList();

        // Newest first: the handler fault, then the app-wide warning.
        Assert.Equal(2, rows.Count);
        Assert.Contains("the save failed", rows[0]);
        Assert.Contains("handler", rows[0]);
        Assert.Contains("InvalidOperationException", rows[0]);
        Assert.Contains("Board › Row", rows[0]);
        Assert.Contains("caught by an error boundary", rows[0]);
        Assert.Contains("10:00:02", rows[0]);
        Assert.Contains("two siblings share a key", rows[1]);
        Assert.Contains("app-wide", rows[1]);
        Assert.Contains("warning", rows[1]);
    }

    [Fact]
    public async Task The_filters_show_this_page_or_the_app_wide_errors()
    {
        var (page, app) = Logs();
        var tab = Tab(page, app);

        await Click(tab, "This page");

        Assert.Contains("the save failed", tab.Html);
        Assert.DoesNotContain("two siblings", tab.Html);

        await Click(tab, "App-wide");

        Assert.DoesNotContain("the save failed", tab.Html);
        Assert.Contains("two siblings", tab.Html);
    }

    [Fact]
    public async Task Only_a_likely_framework_bug_offers_a_report_which_opens_a_github_issue_to_review()
    {
        var (page, app) = Logs();
        page.Record(DevToolsErrorKind.Render, false, "NullReferenceException", "customer 4711 has no card", "stack", "Row", 9,
            false, false, At.AddSeconds(5),
            new DevToolsStackVerdict(true, ["Rask.Core.Live.FrameDiffer.DiffSiblings", "[app code]"]));
        var tab = Tab(page, app);

        // One button: the handler fault from the app's own code has none.
        Assert.Single(tab.FindAll("button"), b => b.TextContent.Trim() == "Report framework bug");

        await Click(tab, "Report framework bug");

        var link = tab.Find("a[target=\"_blank\"]");
        var href = System.Net.WebUtility.HtmlDecode(link.Attributes["href"]);
        Assert.StartsWith(DevToolsBugReport.NewIssueUrl + "?labels=bug&title=", href);
        Assert.Contains("FrameDiffer.DiffSiblings", Uri.UnescapeDataString(href));
        Assert.DoesNotContain("4711", href);
        Assert.Equal("noopener noreferrer", link.Attributes["rel"]);
        Assert.Contains("Chrome 131", tab.Find("textarea").TextContent);
    }

    [Fact]
    public async Task Show_in_tree_hands_over_the_components_tree_id()
    {
        var (page, app) = Logs();
        long? shown = null;
        var tab = Tab(page, app, id => shown = id);

        await Click(tab, "Show in tree");

        Assert.Equal(42, shown);
    }

    [Fact]
    public void The_strip_counts_unseen_errors_not_warnings_and_tells_the_page()
    {
        var (page, app) = Logs();

        var strip = Tabs(DevToolsTabIds.Wire, page, app);

        Assert.Equal("1", strip.Find("[data-rask-devtools-errors]").Attributes["data-rask-devtools-errors"]);
        Assert.Equal("Errors1", strip.FindAll("[role=\"tab\"]")[^1].TextContent.Replace(" ", "").Trim());
    }

    [Fact]
    public void On_the_errors_tab_everything_listed_counts_as_seen()
    {
        var (page, app) = Logs();

        var strip = Tabs(DevToolsTabIds.Errors, page, app);

        Assert.Equal("0", strip.Find("[data-rask-devtools-errors]").Attributes["data-rask-devtools-errors"]);
    }

    [Fact]
    public async Task The_page_asking_to_show_the_errors_selects_the_errors_tab()
    {
        var (page, app) = Logs();
        string? selected = null;
        var strip = Tabs(DevToolsTabIds.Wire, page, app, id => selected = id);

        await strip.On("[data-rask-devtools-errors]").RaiseAsync("keydown", "{\"key\":\"errors:show\"}");

        Assert.Equal(DevToolsTabIds.Errors, selected);
    }
}
