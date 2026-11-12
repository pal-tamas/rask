using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // small test pages; their <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Cached.Name(…)[Report]</c> on a live Server page (#1323): the first request waits for the report's data and
///     stores what it drew; every later request — another visitor, another tab — is handed that markup with the report
///     never mounted; and in the live page the stored markup is an island the diff leaves alone while the page around it
///     re-renders.
/// </summary>
public sealed class CachedTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task The_first_response_waits_for_the_data_and_a_later_one_is_served_the_same_markup_without_a_mount()
    {
        await using var session = await HookSession.OpenAsync<CachedPage>(playwright);
        using var http = new HttpClient();
        await Expect(session.Page.Locator("#report caption")).ToContainTextAsync("draw");
        var mounts = CachedReportProbe.Mounts;

        var first = await http.GetStringAsync(session.BaseUrl + "/", TestContext.Current.CancellationToken);
        var second = await http.GetStringAsync(session.BaseUrl + "/", TestContext.Current.CancellationToken);

        Assert.Contains("North", first, StringComparison.Ordinal);
        Assert.DoesNotContain("loading", first, StringComparison.Ordinal);
        Assert.Equal(Caption(first), Caption(second));
        Assert.Equal(mounts, CachedReportProbe.Mounts);
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task The_stored_report_stays_one_untouched_island_while_the_live_page_around_it_renders(int width, int height)
    {
        await using var session = await HookSession.OpenAsync<CachedPage>(
            playwright, new BrowserNewContextOptions { ViewportSize = new() { Width = width, Height = height } });
        var page = session.Page;
        await Expect(page.Locator("#report caption")).ToContainTextAsync("draw");
        await page.Locator("#report").EvaluateAsync("t => t.__kept = true");
        var caption = await page.Locator("#report caption").TextContentAsync();

        for (var click = 1; click <= 3; click++)
        {
            await page.ClickAsync("#bump");
            await Expect(page.Locator("#count")).ToHaveTextAsync($"clicked {click}");
        }

        Assert.True(await page.Locator("#report").EvaluateAsync<bool>("t => t.__kept === true"), "the island was replaced");
        await Expect(page.Locator("#report caption")).ToHaveTextAsync(caption!);
        await Expect(page.Locator("#report tbody tr")).ToHaveCountAsync(3);
        Assert.True(await page.Locator("#report").IsVisibleAsync());
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task A_second_visitor_is_handed_the_first_visitors_draw_and_the_report_never_mounts_for_them()
    {
        await using var session = await HookSession.OpenAsync<CachedPage>(playwright);
        await Expect(session.Page.Locator("#report caption")).ToContainTextAsync("draw");
        var caption = await session.Page.Locator("#report caption").TextContentAsync();
        var mounts = CachedReportProbe.Mounts;
        var other = await playwright.Browser.NewContextAsync();
        var second = await other.NewPageAsync();

        await second.GotoAsync(session.BaseUrl + "/");
        await Expect(second.Locator("#report caption")).ToHaveTextAsync(caption!);
        await second.ClickAsync("#bump");
        await Expect(second.Locator("#count")).ToHaveTextAsync("clicked 1");

        Assert.Equal(mounts, CachedReportProbe.Mounts);
        await other.CloseAsync();
    }

    private static string Caption(string html)
    {
        var start = html.IndexOf("<caption>", StringComparison.Ordinal);
        var end = html.IndexOf("</caption>", start, StringComparison.Ordinal);
        return html[start..end];
    }
}

/// <summary>A live counter beside a cached report.</summary>
public sealed partial class CachedPage : Component
{
    private int _clicks;

    protected override Component? HeadAssets => Markup.Title["cached"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        Button.Id("bump").OnClick(() => _clicks++)["bump"],
        P.Id("count")[$"clicked {_clicks}"],
        Cached.Name("e2e-report").AcrossUsers().For(TimeSpan.FromMinutes(10))[CachedReportProbe]
    ];
}

/// <summary>Reads its data in OnMount, as a dashboard does, and says which draw it is.</summary>
public sealed partial class CachedReportProbe : Component
{
    private static int _all;
    private int _draw;
    private bool _loaded;

    /// <summary>Every mount of every instance.</summary>
    internal static int Mounts => Volatile.Read(ref _all);

    protected override async Task OnMount()
    {
        _draw = Interlocked.Increment(ref _all);
        await Task.Delay(150);
        _loaded = true;
    }

    protected override Component? Render() =>
        _loaded
            ? Table.Id("report")[
                Caption[$"draw {_draw}"],
                Tbody[Tr[Td["North"], Td["12 400"]], Tr[Td["South"], Td["9 870"]], Tr[Td["East"], Td["15 210"]]]
            ]
            : P["loading"];
}
