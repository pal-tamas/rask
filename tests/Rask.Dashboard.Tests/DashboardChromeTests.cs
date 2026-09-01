using Microsoft.Extensions.Hosting;
using Rask.Core.Routing;
using Rask.Testing;

namespace Rask.Dashboard.Tests;

/// <summary>
/// The console's chrome: a breadcrumb bar saying WHAT you are looking at, over a tab bar saying WHICH PART
/// of the console you are in.
/// <para>
/// Every registered queue used to get its own top-level tab, so a deployment running jobs, outbox and mail
/// spent half its navigation on them and the nav grew with the batteries instead of describing the console.
/// These pin the split, because growing it back would look like an improvement in a diff.
/// </para>
/// <para>
/// Rendered through the shell rather than by constructing the layout: the layout renders <c>Outlet</c>, and
/// an outlet outside a router has no route context to resolve against. Going through the shell is also
/// closer to what a browser gets.
/// </para>
/// <para>
/// Asserted through selectors rather than <c>Assert.Contains</c> on the markup. A substring assertion over
/// HTML passes on a class name, an aria value or a comment that merely happens to contain the word — and
/// the negative form is worse: <c>DoesNotContain("Jobs")</c> also passes on a page that failed to render.
/// </para>
/// </summary>
public sealed partial class DashboardChromeTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task Every_registered_queue_shares_one_section_tab()
    {
        // All four batteries registered and mapped: three queues plus the cache.
        await using var h = Harness(Batteries.All);

        // Five sections, whatever the deployment runs — not one per battery.
        Assert.Equal(["Overview", "Queues", "Cache", "Logs", "System"], Sections(h, "/_rask"));
    }

    [Fact]
    public async Task A_deployment_with_no_queues_gets_no_queues_tab()
    {
        // The tab would otherwise be a link to a queue that does not exist.
        await using var h = Harness(Batteries.Cache);

        Assert.Equal(["Overview", "Cache", "Logs", "System"], Sections(h, "/_rask"));
    }

    [Fact]
    public async Task The_queue_switcher_appears_only_while_a_queue_is_open()
    {
        await using var h = Harness(Batteries.All);

        // Elsewhere the crumb would assert a scope the page below it does not have.
        Assert.False(RenderChrome(h, "/_rask").Exists("header select"));

        var onQueue = RenderChrome(h, "/_rask/queues/jobs");
        Assert.True(onQueue.Exists("header select"));

        // Ordered by title, and only the batteries actually available.
        var options = onQueue.FindAll("header select option").Select(o => o.TextContent.Trim()).ToList();
        Assert.Equal(["Jobs", "Mail", "Outbox"], options);
    }

    [Fact]
    public async Task The_open_section_is_the_only_one_marked_current()
    {
        await using var h = Harness(Batteries.All);

        Assert.Equal(["Logs"], Current(h, "/_rask/logs"));
    }

    [Fact]
    public async Task A_queue_page_marks_the_queues_tab_current_whichever_queue_is_open()
    {
        // The tab is a prefix match, so it stays lit on jobs, outbox and mail alike — the crumb below it is
        // what says which one. Overview is an exact match, or it would light up on every path here.
        await using var h = Harness(Batteries.All);

        Assert.Equal(["Queues"], Current(h, "/_rask/queues/outbox"));
    }

    // Development, because the fail-closed default denies everyone in Production and the chrome under test
    // sits behind that policy. The "unsecured" banner it turns on is harmless here.
    private static DashboardHarness Harness(Batteries batteries) =>
        new(batteries, environment: Environments.Development);

    // The brand is a link in the <header>, not in the <nav>, so it is not a section.
    private List<string> Sections(DashboardHarness h, string path) =>
        RenderChrome(h, path).FindAll("nav a").Select(a => a.TextContent.Trim()).ToList();

    private List<string> Current(DashboardHarness h, string path) =>
        RenderChrome(h, path)
            .FindAll("nav a[aria-current=\"page\"]")
            .Select(a => a.TextContent.Trim())
            .ToList();

    private RenderedComponent<RaskDashboardShell> RenderChrome(DashboardHarness h, string path)
    {
        h.Get<RouteState>().Path = path;
        return RaskTest.RenderDocument(RaskDashboardShell, h.Services);
    }
}
