using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     A sidebar narrowed to its rail, walked with a pointer: what parity cannot see, because nothing of it is on the
///     page as loaded.
/// </summary>
/// <remarks>
///     1. Flux's live demo (fluxui.dev/demo/sidebar-collapsible) and the Rask page of the same demo
///     (artifacts/flux-parity/rask/sidebar/collapsible-sidebar.html, given the runtime's hooks), light and dark: every
///     step must land the same box in the same colours — an item's tooltip while the sidebar is wide (opened, not drawn)
///     and in the rail, the collapse control's tooltip in both, the menu a group opens under the pointer, its heading and
///     rows, a row under the pointer, and the menu gone when the pointer leaves; then the same menu in a viewport that
///     ends just under its button, where it has to be slid up and held to a height.
///     2. An application's menu (long-menu.html: twelve groups, eighty items, three folded, one current): the sidebar
///     scrolls inside itself and the page does not; what the rail does to it. Printed, and failed only where the kit
///     would be wrong whatever Flux does (a menu or a tooltip outside the viewport).
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask pages
///     flux rail
///     </code>
///     Exit code 1 on any difference.
/// </remarks>
[FluxCommand("rail", "the sidebar's rail walked with a pointer, against Flux's demo")]
public sealed class Rail : IFluxCommand
{
    private sealed record Side(string Url, string Mark, string Collapse);

    // Every popover showing: its words, its box, its ground and its edge. One that is open and not drawn has no box.
    private const string Showing = """
        () => [...document.querySelectorAll('[popover]')].filter(p => p.matches(':popover-open')).map(p => {
          const box = p.getBoundingClientRect();
          const style = getComputedStyle(p);
          const drawn = style.display !== 'none';
          return [p.getAttribute('role') ?? 'menu', drawn ? [box.x, box.y, box.width, box.height].map(Math.round).join(',') : 'not drawn',
            // The colour of an edge 0px wide is nobody's: not said.
            drawn ? `${style.backgroundColor} ${style.color} ${style.borderTopWidth}${style.borderTopWidth === '0px' ? '' : ` ${style.borderTopColor}`}` : ''].join(' ').replaceAll(' none)', ' 0)');
        }).sort()
        """;

    private const string Look = """
        node => {
          const box = node.getBoundingClientRect();
          const style = getComputedStyle(node);
          // `oklch(… none)` is `oklch(… 0)`: a hue that is missing and one that is zero draw the same grey.
          return `${[box.x, box.y, box.width, box.height].map(Math.round).join(',')} ${style.color} ${style.backgroundColor} ${style.padding}`.replaceAll(' none)', ' 0)');
        }
        """;

    private int failed;
    private IBrowser browser = null!;

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var pages = Repo.Parity("rask", "sidebar");
        var flux = new Side("https://fluxui.dev/demo/sidebar-collapsible", "flux", "[data-flux-sidebar-collapse] button");
        var rask = new Side(Repo.FileUrl(Path.Combine(pages, "collapsible-sidebar.html")), "ui", "[data-ui-sidebar-collapse] label");
        browser = await Browsers.Chromium();

        foreach (var scheme in new[] { "light", "dark" })
        {
            var (theirs, mine) = (await Walk(flux, scheme), await Walk(rask, scheme));
            for (var index = 0; index < theirs.Count; index++)
            {
                var line = theirs[index];
                var other = index < mine.Count ? mine[index] : "undefined";
                var same = line == other;
                if (!same)
                {
                    failed++;
                }

                var step = line.Split(':')[0];
                Console.WriteLine(same ? $"ok   {scheme} {step}" : $"FAIL {scheme} {step}\n       flux {line}\n       rask {other}");
            }
        }

        await LongMenu(Repo.FileUrl(Path.Combine(pages, "long-menu.html")));
        await browser.CloseAsync();
        Console.WriteLine(failed > 0 ? $"\nflux rail: {failed} difference(s)." : "\nflux rail: the rail behaves as Flux's.");
        return failed > 0 ? 1 : 0;
    }

    private static async Task<string> Showings(IPage page) =>
        string.Join(" | ", (await page.Run(Showing) as JsonArray ?? []).Select(Js.Str)) is { Length: > 0 } s ? s : "nothing";

    private static async Task<string> LookOf(IPage page, string selector, int index = 0) => Js.Str(await page.Locator(selector).Nth(index).Run(Look)) ?? "";

    private async Task<IPage> Open(Side side, string scheme, int width = 1280)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 900 },
            ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(side.Url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
        if (side.Mark == "ui")
        {
            await Runtime.With(page, "rask-hooks.ts");
        }

        await page.Run("() => document.fonts.ready.then(() => undefined)");
        return page;
    }

    private static Task Settle(IPage page) => page.WaitForTimeoutAsync(350);

    private static async Task Away(IPage page)
    {
        await page.Mouse.MoveAsync(900, 500);
        await Settle(page);
    }

    // One side's walk, as lines. The same steps on both; the selectors differ only by the marker's prefix.
    private async Task<List<string>> Walk(Side side, string scheme)
    {
        var page = await Open(side, scheme);
        var m = side.Mark;
        var item = $"[data-{m}-sidebar-nav] > * > [data-{m}-sidebar-item]";
        var group = $"[data-{m}-sidebar-group-dropdown]";
        var row = $"{group} [data-{m}-menu] [data-{m}-sidebar-item]";
        var lines = new List<string>();
        void Say(string step, string value) => lines.Add($"{step}: {value}");

        await page.Locator(item).Nth(1).HoverAsync();
        await Settle(page);
        Say("wide, pointer on an item", await Showings(page));
        await page.Locator(side.Collapse).Locator("visible=true").First.HoverAsync();
        await Settle(page);
        Say("wide, pointer on the collapse control", await Showings(page));

        await page.Locator(side.Collapse).Locator("visible=true").First.ClickAsync();
        await Away(page);
        foreach (var index in new[] { 0, 1, 2 })
        {
            await page.Locator(item).Nth(index).HoverAsync();
            await Settle(page);
            Say($"rail, pointer on item {index}", await Showings(page));
        }

        // From beside it, level with it: no item is crossed on the way, so no item's tooltip is left behind.
        var button = (await page.Locator($"{group} > button").BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(400, button.Y + button.Height / 2);
        await Settle(page);
        await page.Mouse.MoveAsync(button.X + button.Width / 2, button.Y + button.Height / 2, new MouseMoveOptions { Steps = 4 });
        await Settle(page);
        Say("rail, pointer on the group", await Showings(page));
        // A menu the pointer opened takes no focus and locks nothing: Flux leaves both as they were.
        Say("focus and the page behind the group's menu", Js.Str(await page.Run("""
            () => {
              const html = getComputedStyle(document.documentElement);
              const inMenu = [...document.querySelectorAll('[popover]')].some(p => p.matches(':popover-open') && p.contains(document.activeElement));
              return `focus ${inMenu ? 'in the menu' : 'where it was'}, ${html.overflowY} ${html.pointerEvents}`;
            }
            """)) ?? "");
        Say("the group button under the pointer", await LookOf(page, $"{group} > button"));
        Say("the heading", await LookOf(page, $"{group} [data-{m}-menu-heading]"));
        Say("row 0", await LookOf(page, row, 0));
        // Straight across the gap, then down the menu: a diagonal would leave both, and close it on either side.
        await page.Mouse.MoveAsync(button.X + button.Width + 20, button.Y + button.Height / 2);
        await Settle(page);
        var second = (await page.Locator(row).Nth(1).BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(second.X + 40, second.Y + second.Height / 2, new MouseMoveOptions { Steps = 5 });
        await Settle(page);
        Say("row 1 under the pointer", await LookOf(page, row, 1));
        Say("the rows lit under the pointer", Js.Str(await page.Locator(row).RunAll("rows => rows.map((r, i) => (r.hasAttribute('data-active') ? i : -1)).filter(i => i >= 0).join(',') || 'none'")) ?? "");
        Say("pointer on row 1", await Showings(page));
        await Away(page);
        Say("pointer gone", await Showings(page));

        await page.Locator($"[data-{m}-sidebar-header]").HoverAsync();
        await Settle(page);
        Say("rail, pointer on the header", await Showings(page));
        await page.Locator($"[data-{m}-sidebar-profile]").First.HoverAsync();
        await Settle(page);
        Say("rail, pointer on the profile", await Showings(page));

        // A viewport that ends just under the group's button: no room for its menu beside it. Flux slides the menu
        // up to stay 5 px inside the bottom edge, and holds it to the height that is left (the kit: rask-place.ts).
        foreach (var under in new[] { 60, 8 })
        {
            await Away(page);
            await page.SetViewportSizeAsync(1280, (int)Js.Round(button.Y + button.Height + under));
            await Settle(page);
            await page.Mouse.MoveAsync(400, button.Y + button.Height / 2);
            await Settle(page);
            await page.Mouse.MoveAsync(button.X + button.Width / 2, button.Y + button.Height / 2, new MouseMoveOptions { Steps = 4 });
            await Settle(page);
            Say($"rail, the viewport ending {under} px under the group, pointer on it", await Showings(page));
        }

        await page.Context.CloseAsync();
        return lines;
    }

    // ----- An application's menu ------------------------------------------------------------------------
    private async Task LongMenu(string url)
    {
        var page = await Open(new Side(url, "ui", ""), "light");
        void Check(bool ok, string text)
        {
            if (!ok)
            {
                failed++;
            }

            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} long menu: {text}");
        }

        static void Note(string text) => Console.WriteLine($"note long menu: {text}");
        async Task<JsonNode> Sidebar() => (await page.Locator("[data-ui-sidebar]").Run("""
            node => {
              const box = node.getBoundingClientRect();
              return { width: box.width, height: box.height, client: node.clientHeight, scroll: node.scrollHeight, top: node.scrollTop,
                page: document.documentElement.scrollHeight, view: innerHeight };
            }
            """))!;
        async Task<double> Y(string selector) => Js.Number(await page.Locator(selector).First.Run("node => Math.round(node.getBoundingClientRect().y)"));
        static string N(JsonNode? value) => Js.Num(Js.Number(value));
        static double D(JsonNode? value) => Js.Number(value);

        var counts = (await page.Run("""
            () => ({
              groups: document.querySelectorAll('details[data-ui-sidebar-group]').length,
              folded: document.querySelectorAll('details[data-ui-sidebar-group]:not([open])').length,
              items: document.querySelectorAll('details[data-ui-sidebar-group] [data-ui-sidebar-item], [data-ui-sidebar-nav] > * > [data-ui-sidebar-item]').length,
              current: document.querySelectorAll('details [aria-current=page]').length,
            })
            """))!;
        Check(D(counts["groups"]) == 12 && D(counts["items"]) == 80 && D(counts["folded"]) == 3 && D(counts["current"]) == 1,
            $"{N(counts["groups"])} groups, {N(counts["items"])} items, {N(counts["folded"])} folded, {N(counts["current"])} current");

        var wide = await Sidebar();
        Check(D(wide["height"]) == D(wide["view"]) && D(wide["scroll"]) > D(wide["client"]), $"the sidebar is the viewport's height ({N(wide["height"])}) and scrolls inside itself ({N(wide["scroll"])} of content)");
        Check(D(wide["page"]) == D(wide["view"]), "the page itself does not scroll");

        const string current = "details [aria-current=page]";
        var at = await Y(current);
        Note($"the current item is at y={Js.Num(at)} as loaded (viewport {N(wide["view"])}): {(at < D(wide["view"]) ? "in view" : "below the fold — neither Flux nor the kit scrolls to it")}");
        await page.Locator(current).ScrollIntoViewIfNeededAsync();
        var scrolled = await Sidebar();
        Note($"scrolled to it (scrollTop {N(scrolled["top"])}): the header is at y={Js.Num(await Y("[data-ui-sidebar-header]"))}, the profile at y={Js.Num(await Y("[data-ui-sidebar-profile]"))} — the whole sidebar scrolls, as Flux's does");

        // The first top-level item must not move when the rail is toggled; what is below the groups does.
        await page.Locator("[data-ui-sidebar]").Run("node => { node.scrollTop = 0; }");
        async Task<double> MainX() => Js.Number(await page.Locator("[data-ui-main]").Run("node => node.getBoundingClientRect().x"));
        var (homeBefore, mainBefore) = (await Y("[data-ui-sidebar-nav] [data-ui-sidebar-item]"), await MainX());
        await page.Locator("[data-ui-sidebar-collapse] label").Locator("visible=true").First.ClickAsync();
        await Away(page);
        var rail = await Sidebar();
        var (homeAfter, mainAfter) = (await Y("[data-ui-sidebar-nav] [data-ui-sidebar-item]"), await MainX());
        Check(D(rail["width"]) == 56 && homeBefore == homeAfter, $"the rail is 56px and the first item stays at y={Js.Num(homeAfter)}");
        Note($"the page's column moves from x={Js.Num(mainBefore)} to x={Js.Num(mainAfter)}; the rail holds {N(rail["scroll"])}px of icons in {N(rail["client"])}px");
        var icons = await page.Locator("[data-ui-sidebar-group-dropdown] > button").Locator("visible=true").CountAsync();
        Check(icons == 12, $"{icons} group icons in the rail");

        // The last group's menu and the last item's tooltip must be drawn inside the viewport.
        foreach (var (name, selector) in new[] { ("the last group", "[data-ui-sidebar-group-dropdown] > button"), ("the last item", "[data-ui-sidebar-nav] > * > [data-ui-sidebar-item]") })
        {
            var target = page.Locator(selector).Last;
            await target.ScrollIntoViewIfNeededAsync();
            var box = (await target.BoundingBoxAsync())!;
            await Away(page);
            await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2, new MouseMoveOptions { Steps = 3 });
            await Settle(page);
            var shown = (await page.Run("""
                () => [...document.querySelectorAll('[popover]')].filter(p => p.matches(':popover-open') && getComputedStyle(p).display !== 'none').map(p => {
                  const b = p.getBoundingClientRect();
                  return { x: b.x, y: b.y, bottom: b.bottom, right: b.right, words: p.textContent.trim().replace(/\s+/g, ' ').slice(0, 24) };
                })
                """) as JsonArray) ?? [];
            var first = shown.Count > 0 ? shown[0] : null;
            Check(shown.Count == 1 && D(first!["y"]) >= 0 && D(first["bottom"]) <= 900 && D(first["x"]) >= 48,
                $"{name} (y={Js.Num(Js.Round(box.Y))}) opens \"{(first is null ? "undefined" : Js.Str(first["words"]))}\" at {(first is null ? "nowhere" : $"{Js.Num(Js.Round(D(first["x"])))},{Js.Num(Js.Round(D(first["y"])))}")}, inside the viewport");
        }

        // A phone: the same menu slid over the page scrolls inside the overlay.
        await page.Context.CloseAsync();
        var phone = await Open(new Side(url, "ui", ""), "light", 390);
        await phone.Locator("[data-ui-sidebar-toggle]").First.ClickAsync();
        await Settle(phone);
        var overlay = (await phone.Locator("[data-ui-sidebar]").Run("node => ({ x: node.getBoundingClientRect().x, client: node.clientHeight, scroll: node.scrollHeight, overflow: getComputedStyle(node).overflowY })"))!;
        Check(D(overlay["x"]) == 0 && D(overlay["scroll"]) > D(overlay["client"]) && Js.Str(overlay["overflow"]) == "auto",
            $"on a phone the overlay is on screen and scrolls inside itself ({N(overlay["scroll"])} in {N(overlay["client"])})");
        await phone.Context.CloseAsync();
    }
}
