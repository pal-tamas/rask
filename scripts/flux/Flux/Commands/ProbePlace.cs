using System.Globalization;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     Opens one panel again and again with its host held at places of your choosing, on ONE page, and prints where it
///     landed each time and what it kept between two opens: the question a sequence of <c>Placement</c> landings asks
///     without saying so (Flux sizes a panel where it last stood).
/// </summary>
/// <remarks>
///     <code>
///     flux probe-place &lt;url&gt; &lt;host selector&gt; &lt;panel selector&gt; &lt;index&gt; &lt;W&gt;x&lt;H&gt; &lt;step&gt;…
///     flux probe-place https://fluxui.dev/components/popover "[data-preview-wrapper] [data-flux-dropdown]" "[data-flux-popover]" 8 1920x1080 1629,565 mid open 1629,565
///     </code>
///     A step is <c>x,y</c> (the host's left and top; a negative number counts its far edge from the viewport's far edge):
///     hold it there and open; <c>mid</c> the same in the middle; <c>resize:WxH</c> the viewport; <c>reload</c> the page.
///     Each open prints the panel's box, its inline style while open and after it closed.
/// </remarks>
[FluxCommand("probe-place", "where one panel lands, open after open, with its host held at given places")]
public sealed class ProbePlace : IFluxCommand
{
    private const string Hold = """
        ({ host, index, at }) => {
          const el = [...document.querySelectorAll(host)][index];
          if (!el) return null;
          Object.assign(el.style, { position: 'fixed', margin: '0', zIndex: '2147483647', width: 'max-content', left: '0', top: '0' });
          const box = el.getBoundingClientRect();
          const room = [document.documentElement.clientWidth, document.documentElement.clientHeight];
          const place = (v, i) => (v === 'mid' ? Math.round((room[i] - [box.width, box.height][i]) / 2) : v < 0 ? room[i] + v - [box.width, box.height][i] : v);
          const [left, top] = at.map(place);
          el.style.left = `${left}px`;
          el.style.top = `${top}px`;
          const t = el.firstElementChild.getBoundingClientRect();
          return [t.x + t.width / 2, t.y + t.height / 2, t.x, t.right];
        }
        """;

    private const string Look = """
        ({ host, index, panel }) => {
          const el = [...document.querySelectorAll(host)][index].querySelector(panel);
          const b = el.getBoundingClientRect();
          const open = el.matches(':popover-open');
          return `${open ? 'open  ' : 'closed'} x=${b.left.toFixed(1)}..${b.right.toFixed(1)} w=${b.width.toFixed(1)} y=${b.top.toFixed(1)}..${b.bottom.toFixed(1)} vw=${document.documentElement.clientWidth} html=${getComputedStyle(document.documentElement).overflow}/${getComputedStyle(document.documentElement).scrollbarGutter} style="${el.getAttribute('style') ?? ''}"`;
        }
        """;

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        if (args.Length < 6)
        {
            Console.Error.WriteLine("usage: flux probe-place <url> <host> <panel> <index> <W>x<H> <step>…");
            return 1;
        }

        var (url, host, panel) = (args[0], args[1], args[2]);
        var index = int.Parse(args[3], CultureInfo.InvariantCulture);
        var size = args[4].Split('x').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        var browser = await Browsers.Chromium();
        var context = await browser.NewContextAsync(Screen.Of(size[0], size[1]));
        var page = await context.NewPageAsync();
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 120000 });
        await page.Run("() => document.fonts.ready.then(() => undefined)");
        var where = new Dictionary<string, object> { ["host"] = host, ["index"] = index, ["panel"] = panel };
        foreach (var step in args.Skip(5))
        {
            if (step.StartsWith("resize:", StringComparison.Ordinal))
            {
                var to = step[7..].Split('x').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                await page.SetViewportSizeAsync(to[0], to[1]);
                Console.WriteLine($"{step}: {await page.Run(Look, where)}");
                continue;
            }

            if (step == "reload")
            {
                await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
                Console.WriteLine("reload");
                continue;
            }

            object[] at = step == "mid" ? ["mid", "mid"] : [.. step.Split(',').Select(v => (object)int.Parse(v, CultureInfo.InvariantCulture))];
            var held = (await page.Run(Hold, new Dictionary<string, object> { ["host"] = host, ["index"] = index, ["at"] = at }))!.AsArray();
            await page.Mouse.MoveAsync(2, 2);
            await page.Mouse.ClickAsync((float)held[0]!.GetValue<double>(), (float)held[1]!.GetValue<double>());
            await page.WaitForTimeoutAsync(400);
            Console.WriteLine($"{step} trigger x={held[2]!.GetValue<double>():0.#}..{held[3]!.GetValue<double>():0.#}: {await page.Run(Look, where)}");
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(300);
            Console.WriteLine($"   after Escape: {await page.Run(Look, where)}");
        }

        await browser.CloseAsync();
        return 0;
    }
}
