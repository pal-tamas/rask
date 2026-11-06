using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     What Flux tells its SERVER while a reader uses a component: nothing, for anything that only changes what is drawn.
/// </summary>
/// <remarks>
///     Flux handles a pointer over a row, a cursor moving, a popup opening or closing and a view being paged in its own
///     script; Livewire hears a bound value changing and an action the page wrote. This walks Flux's live docs page of
///     each component with a real pointer and keyboard and counts the Livewire update requests each kind of gesture
///     caused. It is the reference the kit's frame budget is held to
///     (tests/Rask.Server.E2E.Tests/KitDrawsWithoutAskingTests.cs): a gesture that costs Flux no request may cost the kit
///     no frame.
///     <code>flux requests [slug ...]         # every component page when none is named</code>
///     Prints one line per (page, gesture) with the number of requests, and writes artifacts/flux-parity/requests.tsv.
///     Only the public docs pages are read.
/// </remarks>
[FluxCommand("requests", "the Livewire requests each gesture costs on Flux's pages")]
public sealed partial class Requests : IFluxCommand
{
    private static readonly string[] Slugs =
    [
        "accordion", "autocomplete", "avatar", "badge", "breadcrumbs", "button", "calendar", "callout", "card", "chart",
        "checkbox", "color-picker", "command", "context", "date-picker", "dropdown", "editor", "field", "file-upload",
        "input", "kanban", "modal", "navbar", "otp-input", "pagination", "pillbox", "popover", "profile", "progress",
        "radio", "select", "separator", "skeleton", "slider", "switch", "table", "tabs", "textarea", "time-picker",
        "timeline", "toast", "tooltip",
    ];

    // The centre of every part a pointer or the keyboard can act on, inside the page's content, in document order.
    private const string Parts = """
        ([selector, most]) => {
          const seen = [];
          for (const el of document.querySelectorAll(selector)) {
            if (el.closest('header, footer, aside, [data-flux-sidebar], [data-flux-header]')) continue;
            const box = el.getBoundingClientRect();
            if (box.width < 2 || box.height < 2 || getComputedStyle(el).visibility === 'hidden') continue;
            seen.push([box.left + scrollX + box.width / 2, box.top + scrollY + box.height / 2]);
            if (seen.length === most) break;
          }
          return seen;
        }
        """;

    private static readonly (string Name, Func<IPage, Task> Run)[] Gestures =
    [
        ("pointer over every part", async page =>
        {
            foreach (var part in await PartsOf(page, "main a, main button, main input, main select, main textarea, main summary, main label, main th, main [role], main [tabindex], main svg", 120))
            {
                var (x, y) = await At(page, part);
                await page.Mouse.MoveAsync(x, y, new MouseMoveOptions { Steps = 3 });
            }
        }),
        ("focus and blur by Tab", async page =>
        {
            await page.Run("() => { scrollTo(0, 0); document.querySelector('main')?.setAttribute('tabindex', '-1'); document.querySelector('main')?.focus(); }");
            for (var i = 0; i < 40; i++)
            {
                await page.Keyboard.PressAsync("Tab");
            }

            for (var i = 0; i < 10; i++)
            {
                await page.Keyboard.PressAsync("Shift+Tab");
            }
        }),
        ("open a popup, walk it with the pointer and the keys, close it", async page =>
        {
            foreach (var trigger in await PartsOf(page, "main [aria-haspopup], main button[aria-expanded], main [role=combobox], main ui-select > button, main ui-tooltip > button", 14))
            {
                var (x, y) = await At(page, trigger);
                await page.Mouse.ClickAsync(x, y);
                await page.WaitForTimeoutAsync(150);
                foreach (var key in new[] { "ArrowDown", "ArrowDown", "ArrowUp", "End", "Home" })
                {
                    await page.Keyboard.PressAsync(key);
                }

                var inside = await page.Run("""
                    () => [...document.querySelectorAll(':popover-open [role], :popover-open button, dialog[open] [role], dialog[open] button')]
                      .slice(0, 8).map(el => { const b = el.getBoundingClientRect(); return [b.left + b.width / 2, b.top + b.height / 2]; })
                    """) as JsonArray ?? [];
                foreach (var point in inside)
                {
                    await page.Mouse.MoveAsync((float)Js.Number(point![0]), (float)Js.Number(point[1]), new MouseMoveOptions { Steps = 3 });
                }

                await page.Keyboard.PressAsync("Escape");
                await page.WaitForTimeoutAsync(100);
                await page.Keyboard.PressAsync("Escape");
            }
        }),
        ("page or switch a view", async page =>
        {
            foreach (var part in await PartsOf(page, "main [role=tab], main summary, main [data-flux-accordion-heading], main button[aria-label*=\"month\" i], main button[aria-label*=\"next\" i], main button[aria-label*=\"previous\" i]", 24))
            {
                var (x, y) = await At(page, part);
                await page.Mouse.ClickAsync(x, y);
                await page.Keyboard.PressAsync("ArrowRight");
                await page.Keyboard.PressAsync("ArrowLeft");
            }
        }),
        ("typing in a field, focus left in it", async page =>
        {
            foreach (var part in await PartsOf(page, "main input:not([type=checkbox]):not([type=radio]):not([type=range]):not([type=file]):not([disabled]):not([readonly]), main textarea:not([disabled])", 8))
            {
                var (x, y) = await At(page, part);
                await page.Mouse.ClickAsync(x, y);
                await page.Keyboard.TypeAsync("al");
                await page.Keyboard.PressAsync("Escape");
            }
        }),
        ("drag, still held, then let go", async page =>
        {
            foreach (var part in await PartsOf(page, "main [role=slider], main input[type=range], main ui-chart, main svg[data-flux-chart-svg]", 6))
            {
                var (x, y) = await At(page, part);
                await page.Mouse.MoveAsync(x - 30, y);
                await page.Mouse.DownAsync();
                await page.Mouse.MoveAsync(x + 60, y, new MouseMoveOptions { Steps = 12 });
                await page.Mouse.UpAsync();
            }
        }),
    ];

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var slugs = args.Length > 0 ? args : Slugs;
        var browser = await Browsers.Chromium();
        var rows = new ConcurrentQueue<string[]>();
        var printing = new Lock();

        // Four pages at a time: each is a page of someone else's site, and the walk is mostly waiting on it.
        var queue = new ConcurrentQueue<string>(slugs);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            while (queue.TryDequeue(out var slug))
            {
                await Walk(browser, slug, row =>
                {
                    rows.Enqueue(row);
                    lock (printing)
                    {
                        Console.WriteLine(string.Join('\t', row));
                    }
                });
            }
        }));

        await browser.CloseAsync();
        var output = Repo.Parity();
        Directory.CreateDirectory(output);
        var sorted = rows.ToList();
        var english = CultureInfo.GetCultureInfo("en").CompareInfo;
        sorted.Sort((a, b) => english.Compare(a[0], b[0], CompareOptions.None));
        await File.WriteAllTextAsync(Path.Combine(output, "requests.tsv"), string.Join('\n', sorted.Select(row => string.Join('\t', row))) + "\n");
        return 0;
    }

    private static async Task Walk(IBrowser browser, string slug, Action<string[]> done)
    {
        foreach (var (gesture, run) in Gestures)
        {
            var context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1280, Height = 900 } });
            var page = await context.NewPageAsync();
            var (requests, posts, counting) = (0, 0, false);
            page.Request += (_, request) =>
            {
                if (counting && request.Method == "POST")
                {
                    Interlocked.Increment(ref posts);
                    if (LivewireUpdate().IsMatch(request.Url))
                    {
                        Interlocked.Increment(ref requests);
                    }
                }
            };
            string[] row;
            try
            {
                await page.GotoAsync($"https://fluxui.dev/components/{slug}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                counting = true;
                await run(page);
                await page.WaitForTimeoutAsync(1200);
                row = [slug, gesture, requests.ToString(CultureInfo.InvariantCulture), posts.ToString(CultureInfo.InvariantCulture)];
            }
            catch (Exception error) when (error is PlaywrightException or TimeoutException)
            {
                row = [slug, gesture, "failed: " + error.Message.Split('\n')[0]];
            }

            done(row);
            await context.CloseAsync();
        }
    }

    private static async Task<List<double[]>> PartsOf(IPage page, string selector, int most) =>
        (await page.Run(Parts, new object[] { selector, most }) as JsonArray ?? []).Select(p => new[] { Js.Number(p![0]), Js.Number(p[1]) }).ToList();

    private static async Task<(float X, float Y)> At(IPage page, double[] part)
    {
        await page.Run("top => scrollTo(0, top)", Math.Max(0, part[1] - 400));
        var top = Js.Number(await page.Run("() => scrollY"));
        return ((float)part[0], (float)(part[1] - top));
    }

    [GeneratedRegex("livewire[^/]*/update")]
    private static partial Regex LivewireUpdate();
}
