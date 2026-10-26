using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Tests.Infrastructure;
using Rask.TestSupport;

namespace Rask.Server.Tests.Routing;

// One request, one document: a first response is rendered in as many waves as its reads take to come back,
// and how many that is depends on when they do — so nothing a page says may depend on it. Reported from a list
// whose rows are read in an awaited OnUpdated: about one GET in a hundred and fifty wired the row's icon buttons
// to their tooltips differently from the rest (IdLinkApp is that page).
[Collection("HostEnvironment")]
public sealed class IdLinksInTheFirstResponseTests(ITestOutputHelper output)
{
    private const int Rows = IdLinkStore.RowCount;
    private const int Sequential = 24;
    private const int Parallel = 16;
    private const int Rounds = 2;

    [Theory]
    [InlineData(IdLinkRead.Synchronous)]
    [InlineData(IdLinkRead.Yielded)]
    [InlineData(IdLinkRead.Delayed)]
    [InlineData(IdLinkRead.FromAPoolThread)]
    public async Task A_list_read_in_an_awaited_hook_wires_every_row_to_its_tooltips_the_same_on_every_request(IdLinkRead read)
    {
        using var host = Host(new IdLinkStore { Read = read });

        var pages = await Get(host, "/id-links/rows");

        Assert.Equal([Wired], pages.Select(Wiring).Distinct());
        Assert.Empty(pages.SelectMany(IdLinks.Broken).Distinct(StringComparer.Ordinal));
        AssertOneDocument(pages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_list_is_wired_the_same_whether_its_two_reads_come_back_together_or_a_wave_apart(bool apart)
    {
        var store = new IdLinkStore();
        using var host = Host(store);
        var settled = IdLinks.Shape(await host.Http.GetStringAsync("/id-links/rows", TestContext.Current.CancellationToken));
        store.Hold();

        var request = host.Http.GetStringAsync("/id-links/rows", TestContext.Current.CancellationToken);
        await store.ReadsStarted(1);
        store.Release();
        if (apart)
        {
            await store.ReadsStarted(2);
        }

        store.Release();
        var held = await ReleasingUntilDone(store, request);

        Assert.Equal(Wired, Wiring(held));
        Assert.Empty(IdLinks.Broken(held));
        Assert.Equal(settled, IdLinks.Shape(held));
    }

    [Theory]
    [InlineData(IdLinkRead.Synchronous)]
    [InlineData(IdLinkRead.Yielded)]
    [InlineData(IdLinkRead.Delayed)]
    [InlineData(IdLinkRead.FromAPoolThread)]
    public async Task Every_id_a_kit_component_makes_up_resolves_and_the_document_is_the_same_on_every_request(IdLinkRead read)
    {
        using var host = Host(new IdLinkStore { Read = read });

        var pages = await Get(host, "/id-links/kit");

        Assert.Empty(pages.SelectMany(IdLinks.Broken).Distinct(StringComparer.Ordinal));
        Assert.DoesNotContain(pages, static page => !page.Contains("id=\"edit-profile\"", StringComparison.Ordinal));
        AssertOneDocument(pages);
    }

    [Fact]
    public async Task A_page_is_the_same_document_however_its_reads_come_back()
    {
        var lists = new List<string>();
        var kits = new List<string>();

        foreach (var read in new[] { IdLinkRead.Synchronous, IdLinkRead.Yielded, IdLinkRead.Delayed, IdLinkRead.FromAPoolThread })
        {
            using var host = Host(new IdLinkStore { Read = read });
            lists.Add(await host.Http.GetStringAsync("/id-links/rows", TestContext.Current.CancellationToken));
            kits.Add(await host.Http.GetStringAsync("/id-links/kit", TestContext.Current.CancellationToken));
        }

        AssertOneDocument(lists);
        AssertOneDocument(kits);
    }

    [Fact]
    public async Task A_list_reached_in_place_is_wired_as_the_one_a_first_request_is_given()
    {
        var store = new IdLinkStore();
        using var host = Host(store);
        var first = await host.Http.GetStringAsync("/id-links/kit", TestContext.Current.CancellationToken);
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, TestContext.Current.CancellationToken);
        await ws.SendJsonAsync(new { type = "hello", session = MarkupAssert.SessionId(first) }, TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, MarkupAssert.SessionId(first));
        await ws.SettledAsync();

        // The page is sent at once and its rows follow when its read is back: every frame up to the last link.
        await ws.SendJsonAsync(new { type = "navigate", path = "/id-links/rows", query = "" }, TestContext.Current.CancellationToken);
        var arrived = Markup(await Through(ws, Rows * IdLinkRowsPage.DescribedPerRow));
        await ws.SendJsonAsync(new { type = "navigate", path = "/id-links/rows", query = "again=1" }, TestContext.Current.CancellationToken);
        var again = Markup(await ws.SettledAsync());

        Assert.Equal(Rows * IdLinkRowsPage.NamedPerRow, IdLinks.Count(arrived, "aria-labelledby", "ui-tooltip-"));
        Assert.Equal(Rows * IdLinkRowsPage.DescribedPerRow, IdLinks.Count(arrived, "aria-describedby", "ui-tooltip-"));
        Assert.DoesNotContain("aria-describedby", again, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-labelledby", again, StringComparison.Ordinal);
    }

    // Sequential requests first — the report's own loop — then rounds of parallel ones.
    private async Task<List<string>> Get(RaskTestHost host, string path)
    {
        var pages = new List<string>();
        for (var i = 0; i < Sequential; i++)
        {
            pages.Add(await host.Http.GetStringAsync(path, TestContext.Current.CancellationToken));
        }

        for (var round = 0; round < Rounds; round++)
        {
            pages.AddRange(await Task.WhenAll(Enumerable.Range(0, Parallel)
                .Select(_ => host.Http.GetStringAsync(path, TestContext.Current.CancellationToken))));
        }

        var described = pages.GroupBy(static page => IdLinks.Count(page, "aria-describedby", "ui-tooltip-"))
            .Select(static group => $"{group.Key} × {group.Count()}");
        output.WriteLine($"{path}: {pages.Count} responses, aria-describedby=\"ui-tooltip-…\" per response: {string.Join(", ", described)}");
        return pages;
    }

    // An icon button is NAMED by its tooltip and one with words is DESCRIBED by it, never both: the tooltips
    // a page has, how many triggers one names, and how many one describes.
    private static readonly (int Tooltips, int Named, int Described) Wired =
        (Rows * (IdLinkRowsPage.NamedPerRow + IdLinkRowsPage.DescribedPerRow), Rows * IdLinkRowsPage.NamedPerRow, Rows * IdLinkRowsPage.DescribedPerRow);

    private static (int Tooltips, int Named, int Described) Wiring(string page) =>
        (IdLinks.Count(page, "id", "ui-tooltip-"), IdLinks.Count(page, "aria-labelledby", "ui-tooltip-"), IdLinks.Count(page, "aria-describedby", "ui-tooltip-"));

    // A wave may start a read of its own (the page's hook runs once, but nothing here should have to know):
    // every read still held when the request has not answered is let go.
    private static async Task<string> ReleasingUntilDone(IdLinkStore store, Task<string> request)
    {
        await WaitFor.True(() =>
        {
            store.Release();
            return request.IsCompleted;
        }, "the held request to answer");

        return await request;
    }

    private static void AssertOneDocument(List<string> pages) =>
        Assert.True(IdLinks.Difference(pages) is not { } difference, IdLinks.Difference(pages));

    // The markup the frames carry, as the browser reads it out of their JSON.
    private static async Task<List<string>> Through(WebSocket ws, int described)
    {
        var frames = new List<string>();
        while (IdLinks.Count(Markup(frames), "aria-describedby", "ui-tooltip-") < described)
        {
            frames.Add(await ws.ReceiveTextAsync());
        }

        return frames;
    }

    private static string Markup(List<string> frames) => string.Concat(frames).Replace("\\\"", "\"", StringComparison.Ordinal);

    private static RaskTestHost Host(IdLinkStore store) =>
        RaskTestHost.Create<IdLinkApp>(
            configureServices: services => services.AddSingleton(store),
            configureServer: static o => o.QuiescenceTimeout = TimeSpan.FromMinutes(5));
}
