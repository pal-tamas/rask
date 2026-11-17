using System.Diagnostics;
using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using Rask.Wasm.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Wasm.E2E.Tests;

/// <summary>
///     An <c>[OffThread]</c> query in a real browser: a large workbook read by <c>Xlsx.Read</c> in the Web Worker, while
///     the page goes on painting — and a cancel that stops a read which never looks at its cancellation, by restarting
///     the worker. Only a browser has the worker, the second runtime and the frame clock this needs.
/// </summary>
public sealed class OffThreadTests(PlaywrightFixture playwright, ITestOutputHelper output) : IClassFixture<PlaywrightFixture>
{
    private static readonly LocatorAssertionsToHaveTextOptions Boot = new() { Timeout = 60_000 };
    private static readonly LocatorAssertionsToHaveTextOptions Settled = new() { Timeout = 60_000 };

    // Counts the page's animation frames: a page whose thread is held paints none.
    private const string FrameCounter =
        "() => { window.__frames = 0; const tick = () => { window.__frames++; requestAnimationFrame(tick); }; requestAnimationFrame(tick); }";

    public static TheoryData<int> Widths => [1280, 390];

    // What the page and its worker say on the console is the journey's log, so a failure shows why.
    private async Task<IPage> Opened(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        page.Console += (_, message) => output.WriteLine($"[console] {message.Text}");
        page.PageError += (_, error) => output.WriteLine($"[page error] {error}");
        page.Worker += (_, worker) => output.WriteLine($"[worker] {worker.Url}");
        return page;
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task The_page_keeps_painting_while_its_worker_reads_a_large_workbook_and_cancel_stops_the_read(int width)
    {
        await using var host = await SignInHost.StartAsync();
        await using var context = await playwright.Browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = 800 } });
        var page = await Opened(context);
        await page.GotoAsync(host.BaseUrl + "/offthread");
        await Expect(page.Locator("#status")).ToHaveTextAsync("idle", Boot);
        await page.ClickAsync("#load");
        await Expect(page.Locator("#status")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^loaded \\d+$"), Settled);
        await page.EvaluateAsync(FrameCounter);

        await page.ClickAsync("#read");
        await Expect(page.Locator("#status")).ToHaveTextAsync("reading", Settled);
        await page.WaitForFunctionAsync("() => typeof window.__frames === 'number'");
        var before = await page.EvaluateAsync<int>("() => window.__frames");
        await page.WaitForTimeoutAsync(1_000);
        var during = await page.EvaluateAsync<int>("() => window.__frames") - before;
        await page.ClickAsync("#cancel");

        Assert.True(during >= 20, $"the page painted {during} frames in the second the worker was reading");
        await Expect(page.Locator("#status")).ToHaveTextAsync("cancelled", new() { Timeout = 5_000 });
        await page.ClickAsync("#ping");
        await Expect(page.Locator("#status")).ToHaveTextAsync("pong", Settled);
        output.WriteLine($"[{width}] frames painted in the second the worker read: {during}");
    }

    [Fact]
    public async Task The_worker_answers_its_first_message_after_starting_and_its_runtime_is_measured()
    {
        await using var host = await SignInHost.StartAsync();
        await using var context = await playwright.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1280, Height = 800 } });
        var page = await Opened(context);
        await page.GotoAsync(host.BaseUrl + "/offthread");
        await Expect(page.Locator("#status")).ToHaveTextAsync("idle", Boot);
        var clock = Stopwatch.StartNew();

        await page.ClickAsync("#ping");

        await Expect(page.Locator("#status")).ToHaveTextAsync("pong", Settled);
        var first = clock.Elapsed;
        var worker = Assert.Single(page.Workers);
        const string Heap = "() => { const r = globalThis.getDotnetRuntime?.(0); return r?.localHeapViewU8?.().length ?? r?.Module?.HEAPU8?.length ?? -1; }";
        var workerHeap = await worker.EvaluateAsync<long>(Heap);
        var pageHeap = await page.EvaluateAsync<long>(Heap);
        output.WriteLine($"first worker answer: {first.TotalMilliseconds:0} ms (from the click; the runtime's files from the browser's cache)");
        output.WriteLine($"worker wasm memory: {workerHeap / 1024 / 1024} MB; page wasm memory: {pageHeap / 1024 / 1024} MB");
        Assert.True(workerHeap > 0, "the worker runs a .NET runtime of its own");
    }
}
