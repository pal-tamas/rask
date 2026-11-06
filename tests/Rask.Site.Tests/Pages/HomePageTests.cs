using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wasm.Browser;

namespace Rask.Site.Tests.Pages;

/// <summary>
/// The landing page leads with the whole stack — data, auth, jobs, realtime, deploy — and the front end
/// second, and pitches what is in the box rather than a benchmark against another framework.
/// </summary>
/// <remarks>
/// The first thing under the hero used to be a byte-for-byte table against Blazor, then a UI-first
/// "batteries" grid. Rask is the full-stack framework now, so the back end takes that place, and the
/// hosts the same components run on close the page.
/// </remarks>
public sealed partial class HomePageTests : global::Rask.Core.RaskMarkup
{
    // Qualified throughout: inside a markup host the bare `HomePage` is the chain entry, not the type.
    private const string WholeStackId = global::Rask.Site.Pages.HomePage.WholeStackSectionId;
    private const string FrontendId = global::Rask.Site.Pages.HomePage.FrontendSectionId;
    private const string HostsId = global::Rask.Site.Pages.HomePage.HostsSectionId;
    private const int BrowserApiCount = global::Rask.Site.Pages.HomePage.BrowserApiCount;

    [Fact]
    public void The_whole_stack_is_the_first_section_after_the_hero()
    {
        var sections = Regex.Matches(Render(), "<section[^>]*>");

        Assert.True(sections.Count >= 2, "the landing page renders fewer than two sections.");
        Assert.Contains($"id=\"{WholeStackId}\"", sections[1].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void The_back_end_comes_before_the_front_end_and_the_hosts()
    {
        var html = Render();
        var stack = html.IndexOf($"id=\"{WholeStackId}\"", StringComparison.Ordinal);
        var frontend = html.IndexOf($"id=\"{FrontendId}\"", StringComparison.Ordinal);
        var hosts = html.IndexOf($"id=\"{HostsId}\"", StringComparison.Ordinal);

        Assert.True(stack >= 0 && frontend >= 0 && hosts >= 0, "a landing-page section is missing.");
        Assert.True(stack < frontend, "the whole-stack section does not come before the frontend section.");
        Assert.True(frontend < hosts, "the frontend section does not come before the hosts section.");
    }

    [Fact]
    public void The_hero_names_the_whole_stack_and_shows_a_feature_back_to_front()
    {
        var html = Render();
        var h1 = Regex.Match(html, "<h1[^>]*>(?<body>.*?)</h1>", RegexOptions.Singleline).Groups["body"].Value;

        Assert.Contains("The whole stack,", h1, StringComparison.Ordinal);
        Assert.Contains("C#", h1, StringComparison.Ordinal);
        Assert.Contains("The full-stack .NET web framework", html, StringComparison.Ordinal);

        // Both files are tabs, and the first — the aggregate — is the pane in the prerendered HTML.
        Assert.Contains(">Product.cs</button>", html, StringComparison.Ordinal);
        Assert.Contains(">ProductsPage.cs</button>", html, StringComparison.Ordinal);
        Assert.Contains(">Aggregate</span>&lt;<span class=\"text-green-700 dark:text-green-400\">Guid</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_hero_code_switches_to_the_page_that_queries_the_aggregate()
    {
        var page = Page.Render(() => HeroCode);
        var html = page.Render();
        Assert.DoesNotContain("QueryClient", html, StringComparison.Ordinal);

        // The second tab's handler: tabs render in file order, and they are the only clickable things.
        var handlers = Regex.Matches(html, "data-rask-on-click=\"(?<id>[^\"]+)\"");
        Assert.Equal(2, handlers.Count);
        await page.Invoke(handlers[1].Groups["id"].Value);

        var after = page.Render();
        Assert.Contains(">QueryClient</span>.Query(", after, StringComparison.Ordinal);
        Assert.Contains("</span>.Count()", after, StringComparison.Ordinal);
        Assert.DoesNotContain(".Read.", after, StringComparison.Ordinal);
        Assert.DoesNotContain(">Aggregate</span>", after, StringComparison.Ordinal);
    }

    /// <summary>Every shipped piece of the back end has a card, and the card opens its guide.</summary>
    [Theory]
    [InlineData("data")]
    [InlineData("query")]
    [InlineData("authentication")]
    [InlineData("jobs")]
    [InlineData("mail")]
    [InlineData("outbox")]
    [InlineData("cache")]
    [InlineData("file-storage")]
    [InlineData("subscriptions")]
    [InlineData("multi-tenancy")]
    [InlineData("full-text-search")]
    [InlineData("dashboard")]
    [InlineData("cli")]
    [InlineData("deployment")]
    public void The_whole_stack_section_links_every_back_end_guide(string guide) =>
        Assert.Contains($"href=\"/docs/guides/{guide}/\"", Slice(WholeStackId), StringComparison.Ordinal);

    [Fact]
    public void The_frontend_section_leads_with_the_live_counter_and_its_source()
    {
        var section = Slice(FrontendId);

        // .count-btn is the browser journey's contract; the source is the front-doors sample.
        Assert.Contains("count-btn", section, StringComparison.Ordinal);
        Assert.Contains(">Counter.cs<", section, StringComparison.Ordinal);
        Assert.Contains($"{BrowserApiCount} typed browser APIs", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// The browser-API count on the page is the number of wrapper services that ship, recounted from the
    /// registrations rather than from a doc — the page, the docs and llms.txt said 50, 51 and 53 at once.
    /// </summary>
    [Fact]
    public void The_browser_api_count_is_every_registered_wrapper()
    {
        var wasmOnly = new ServiceCollection()
            .AddWasmBrowserApis(ServiceLifetime.Singleton)
            .Select(d => d.ServiceType)
            .Where(t => t.IsInterface)
            .Distinct()
            .Count();

        Assert.Equal(BrowserApiCount, RaskHostContracts.BrowserApis.Count + wasmOnly);
    }

    /// <summary>The diagnostics count on the page is the number of sections the guide documents.</summary>
    [Fact]
    public void The_diagnostic_count_is_every_section_the_guide_documents()
    {
        var guide = global::Rask.Site.Features.GuideCatalog.ReadMarkdown("diagnostics")!;

        var sections = guide.Split('\n').Count(line => line.StartsWith("## RASK", StringComparison.Ordinal));

        Assert.Equal(global::Rask.Site.Pages.HomePage.DiagnosticCount, sections);
        Assert.Contains($"{sections} compile-time diagnostics", Slice(FrontendId), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("vs Blazor")]
    [InlineData("vs-blazor")]
    [InlineData("than Blazor")]
    // The owner's positioning: a full-stack framework for any team size, not a one-person slogan, and
    // never framed against another ecosystem's framework.
    [InlineData("One Person Framework")]
    [InlineData("Rails")]
    [InlineData("Ruby")]
    [InlineData("DHH")]
    public void The_page_does_not_say(string phrase) =>
        Assert.DoesNotContain(phrase, Render(), StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("Server", "render-modes")]
    [InlineData("WebAssembly", "pwa")]
    [InlineData("WebAssembly host", "deployment")]
    public void Every_host_is_a_card_into_its_guide(string title, string guide)
    {
        var section = Slice(HostsId);

        Assert.Contains($">{title}</h3>", section, StringComparison.Ordinal);
        Assert.Contains($"href=\"/docs/guides/{guide}/\"", section, StringComparison.Ordinal);
    }

    private static string Render() => Page.Render(() => HomePage).Html;

    // One section of the page, from its id to its closing tag, so a phrase elsewhere cannot satisfy an assertion.
    private static string Slice(string id)
    {
        var html = Render();
        var start = html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the landing page renders no #{id} section.");

        var end = html.IndexOf("</section>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"the #{id} section is never closed.");

        return html[start..end];
    }
}
