using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     Holds the OPEN popover to Flux UI's: how it looks and where it lands, then what a pointer and the keys do.
/// </summary>
/// <remarks>
///     <c>parity</c> measures a page as it loads, and a popover loads closed. The popup is the component, so this
///     measures Flux's page once per dropdown with that dropdown open — <see cref="Measure" /> opens whatever carries
///     <c>data-parity-open</c>, by a real press or a real hover — and files the results as the page <c>popover-open</c>.
///     PopoverOpenParity writes the Rask page of the same name, one example per dropdown with the same mark on its
///     trigger, and <c>parity</c> compares the two as it compares any page: the panel's box, look and contents, and where
///     it sits against the dropdown it belongs to. It is not an <see cref="OpenList" /> kind: that opens ONE list of rows
///     per example and compares it by its own minimum of parity's rules; this page has up to four dropdowns in an
///     example, and what a panel holds is other pages' components.
///     <para>
///         Then the walk: the same presses, pointer moves and keys on Flux's first two examples — the pressed popover
///         and the hovered one — and on the Rask page, each step writing down whether the panel is open, where focus is,
///         what the trigger says and whether the page behind is locked. Every line must agree, but for what a walk names
///         as <c>Open</c> (none does today): a difference that is the runtime's hooks' to close, printed as OPEN and not
///         failed.
///     </para>
///     <para>
///         Last, WHERE IT LANDS with no room: every dropdown of the page opened with its trigger held at the edges of a
///         phone and of a desktop, and with more in the panel than fits (<see cref="Placement" />).
///     </para>
///     <para>
///         The Rask page is static markup. What Flux does in script the kit asks the runtime for (data-rask-hover,
///         data-rask-lock, data-rask-place, the popover's focus rules), so the page is given the runtime's hooks
///         (<see cref="Runtime" />).
///     </para>
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask pages
///     flux parity-popover [--refresh] [--all]                      # --refresh re-measures Flux
///     flux parity-popover --record                                 # Flux's walk alone, every step
///     flux parity-popover --view 390x844                           # the same on a phone (Phone, below)
///     </code>
///     Exit code 1 on any difference.
/// </remarks>
[FluxCommand("parity-popover", "the open popover, its walks and where it lands, against Flux's")]
public sealed class ParityPopover : IFluxCommand
{
    private const string FluxUrl = "https://fluxui.dev/components/popover";
    private const string Hooks = "<!-- runtime hooks -->";

    // A walk's accepted difference: why, and — given — how Flux's line reads once it is the runtime's to close.
    private sealed record Known(string Reason, Func<string, string>? As = null);

    private sealed record Walk(string Name, int Dropdown, string[] Steps, Known? Open = null);

    // What a reader does to a popover that a press opens, and to one the pointer opens. Every pointer step is ONE move,
    // and the pointer crosses the gap between a trigger and its panel in one too ('hoverpanel'): both close a panel the
    // pointer is over neither part of.
    private static readonly string[] Pressed = ["click", "wheel", "clickpanel", "Tab", "Escape", "click", "click", "click", "outside", "focus", "Enter", "Escape",
        "focus", "Space", "Tab", "Shift+Tab", "Shift+Tab", "focus", "ArrowDown", "hover", "leave"];

    private static readonly string[] Hovered = ["hover", "hoverpanel", "leave", "hover", "click", "leave", "focus", "Enter", "Tab", "Escape", "focus", "Space", "Escape"];

    // Measured on Flux (2026-10-12): the pointer leaving closes its panel from a pointer-move check that runs at most
    // once in 100 ms, so a move made straight after another one is not looked at — a pointer gliding off the trigger
    // keeps the panel for up to that long, and reaches it across a narrow gap ('glide': two moves with nothing between
    // them, the second into the gap). A pointer that rested and then goes into the gap in one move ('gap') closes it at
    // once. The runtime's hover hook looks the same way (rask-hover.ts).
    private static readonly string[] Glided = ["hover", "gap", "hover", "glide", "hoverpanel", "glide", "gap", "hover", "leave"];

    // A panel the pointer left takes focus nowhere, on both: the runtime's menu keys hand focus to the trigger of a
    // popover that closed with focus on nothing, but not of one the pointer walked away from (rask-menu-keys.ts).
    private static readonly Walk[] Desktop =
    [
        new("a press opens it", 0, Pressed),
        new("the pointer opens it", 1, Hovered),
        new("the pointer glides across the gap", 1, Glided),
    ];

    // On a phone a press is a tap and the wheel a swipe, and there is no pointer to rest anywhere: the hover steps are
    // not taken, so the hovered popover is walked by taps and keys, and the glide across the gap has no walk.
    private static readonly string[] Pointing = ["hover", "hoverpanel", "leave", "glide", "gap"];

    private static readonly Walk[] Phone = new[]
    {
        new Walk("a tap opens it", 0, Pressed),
        new Walk("a tap on the hovered one", 1, Hovered),
    }.Select(walk => walk with { Steps = walk.Steps.Where(step => !Pointing.Contains(step)).ToArray() }).ToArray();

    // FIRST_TOUCH. Measured on Flux (2026-10-27): the FIRST touch of a page, if it lands on a hover trigger, opens the
    // panel on its pointerenter and its click toggles it shut again; from then on a hover trigger answers a tap as any
    // popover's button does, open and shut. The kit has no such first touch, so both pages are touched once, away from
    // every example, before they are walked or measured (Measure too): what is held to Flux is the page in use.

    // Runs in the page. What a step left behind, in words both pages can be held to.
    private const string Read = """
        (dropdown, { prefix, scrolled }) => {
          const trigger = dropdown.firstElementChild;
          const panel = dropdown.querySelector(`[data-${prefix}-popover]`);
          const active = document.activeElement;
          const focus = active === trigger ? 'on the trigger' : active === panel ? 'on the panel'
            : panel.contains(active) ? 'inside the panel' : active === document.body ? 'on nothing' : 'elsewhere';
          const html = getComputedStyle(document.documentElement);
          const locked = html.overflow === 'hidden' && html.pointerEvents === 'none';
          return [
            panel.matches(':popover-open') ? 'open  ' : 'closed',
            `focus ${focus}`,
            `aria-expanded=${trigger.getAttribute('aria-expanded')}`,
            locked ? 'page locked' : 'page free',
            `scrolled ${Math.round(scrollY - scrolled)}`,
          ].join(' · ');
        }
        """;

    // Runs in the page. Leaves the one example that holds this dropdown, and marks its trigger for Measure to open.
    private const string Mark = """
        (dropdown) => {
          const mine = [...document.querySelectorAll('[data-preview-wrapper] [data-flux-dropdown]')][dropdown];
          const wrapper = mine?.closest('[data-preview-wrapper]');
          for (const other of document.querySelectorAll('[data-preview-wrapper]')) {
            if (other !== wrapper) other.removeAttribute('data-preview-wrapper');
          }

          mine?.firstElementChild.setAttribute('data-parity-open', mine.hasAttribute('hover') ? 'hover' : '');
          return Boolean(mine);
        }
        """;

    // Runs in the page. The same answer to "is it open" for three frames, and no transition running on the panel.
    private const string Settled = """
        el => new Promise(done => {
          let last = null;
          let same = 0;
          let frames = 0;
          const look = () => {
            const now = el.matches(':popover-open');
            same = now === last ? same + 1 : 0;
            last = now;
            if ((same >= 3 && frames >= 12) || ++frames > 120) done();
            else requestAnimationFrame(look);
          };
          requestAnimationFrame(look);
        })
        """;

    private IBrowser browser = null!;
    private View view = null!;

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var arg = new Args(args);
        view = View.Of(arg);
        var walks = view.Phone ? Phone : Desktop;
        var output = Repo.Parity("flux", "popover-open");
        var file = Path.Combine(output, $"measurements{view.Sized}.json");
        var raskPage = Repo.Parity("rask", "popover-open.html");

        browser = await Browsers.Chromium();
        if (arg.Flag("record"))
        {
            foreach (var walk in walks)
            {
                Print(walk, await WalkOne(FluxUrl, walk, "flux"), null);
            }

            await browser.CloseAsync();
            return 0;
        }

        if (!File.Exists(raskPage))
        {
            Console.Error.WriteLine($"flux parity: {raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages");
            return 1;
        }

        // A page that is opened by hover and walked gets the runtime: FluxParityPages writes it without. Written again on
        // every run, so the hooks on the page are the hooks in the tree.
        var html = await File.ReadAllTextAsync(raskPage);
        var bare = html.Contains(Hooks, StringComparison.Ordinal) ? $"{html[..html.IndexOf(Hooks, StringComparison.Ordinal)]}</body></html>" : html;
        // A view of its own gets a page of its own (parity reads `popover-open@390x844.html`), so a desktop run and a
        // phone run at once never read a page the other is writing.
        var walked = view.Sized.Length > 0 ? raskPage.Replace(".html", $"{view.Sized}.html", StringComparison.Ordinal) : raskPage;
        var script = $"{Hooks}<script>{Runtime.Bundle("rask-hooks.ts").Replace("</script", "<\\/script", StringComparison.Ordinal)}</script></body>";
        var at = bare.IndexOf("</body>", StringComparison.Ordinal);
        await File.WriteAllTextAsync(walked, at < 0 ? bare : bare[..at] + script + bare[(at + "</body>".Length)..]);

        if (!File.Exists(file) || arg.Flag("refresh"))
        {
            var open = new JsonObject { ["light"] = new JsonArray(), ["dark"] = new JsonArray() };
            for (var dropdown = 0; ; dropdown++)
            {
                var found = false;
                var index = dropdown;
                var measured = await Measure.MeasurePage(browser, FluxUrl, Path.Combine(output + view.Sized, dropdown.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    async (page, _) => found = Js.Truthy(await page.Run(Mark, index)), [view.Width, view.Height]);
                if (!found)
                {
                    break;
                }

                foreach (var scheme in new[] { "light", "dark" })
                {
                    var example = (measured[scheme] as JsonArray)?.FirstOrDefault()?.DeepClone() as JsonObject ?? [];
                    example["section"] = "open";
                    example["ordinal"] = dropdown;
                    ((JsonArray)open[scheme]!).Add(example);
                }

                Console.WriteLine($"flux: dropdown {dropdown} measured open");
            }

            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(file, Js.Stringify(open));
        }

        var differs = 0;
        foreach (var walk in walks)
        {
            var theirs = await WalkOne(FluxUrl, walk, "flux");
            var mine = await WalkOne(Repo.FileUrl(walked), walk, "ui");
            var off = walk.Steps.Where((_, i) => theirs[i] != mine[i]).Count();
            // A walk's open difference, and nothing else: with `As`, every line has to agree once Flux's is read that way.
            var known = off > 0 && walk.Open is not null
                && (walk.Open.As is null || walk.Steps.Select((_, i) => theirs[i] == mine[i] || walk.Open.As(theirs[i]) == mine[i]).All(ok => ok));
            differs += off > 0 && !known ? 1 : 0;
            Console.WriteLine($"{(off == 0 ? "ok  " : known ? "OPEN" : "FAIL")} {walk.Name} — {walk.Steps.Length} steps{(off > 0 ? $", {off} differ" : "")}{(known ? $" ({walk.Open!.Reason})" : "")}");
            if (off > 0 || arg.Flag("all"))
            {
                Print(walk, theirs, mine);
            }
        }

        Console.WriteLine(differs > 0 ? $"\nflux parity: the popover walks differently in {differs} walk(s).\n" : "\nflux parity: the popover walks as Flux's does.\n");

        // Where it lands is held on a phone and a desktop alike by the desktop run (Placement.Views).
        var astray = 0;
        if (view.Sized.Length == 0)
        {
            var dropdowns = await Count(FluxUrl);
            astray = await Placement.Landings(browser, new Placement.Family("popover",
                new Placement.Side(FluxUrl, "[data-preview-wrapper] [data-flux-dropdown]", "[data-flux-popover]", null),
                new Placement.Side(Repo.FileUrl(Repo.Parity("rask", "popover.html")), "[data-preview-wrapper] [data-ui-dropdown]", "[data-ui-popover]", null),
                dropdowns.Select((hover, index) => hover ? new Placement.Host(index, "hover") : new Placement.Host(index)).ToList())
            {
                Refresh = arg.Flag("refresh"),
                All = arg.Flag("all"),
            });
        }

        await browser.CloseAsync();
        if (view.Sized.Length == 0)
        {
            Console.WriteLine(astray > 0 ? $"\nflux parity: the popover lands elsewhere for {astray} dropdown(s).\n" : "\nflux parity: the popover lands where Flux's does.\n");
        }

        differs += astray;
        var parity = await new Parity().Run(["popover-open", .. args.Where(a => a != "--refresh")]);
        return differs > 0 || parity != 0 ? 1 : 0;
    }

    // Flux's dropdowns in page order: whether the pointer opens each.
    private async Task<List<bool>> Count(string url)
    {
        var page = await browser.NewPageAsync();
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 120000 });
        var hovered = await page.Run("() => [...document.querySelectorAll('[data-preview-wrapper] [data-flux-dropdown]')].map(el => el.hasAttribute('hover'))");
        await page.CloseAsync();
        return (hovered as JsonArray ?? []).Select(Js.Truthy).ToList();
    }

    private static void Print(Walk walk, List<string> theirs, List<string>? mine)
    {
        for (var i = 0; i < walk.Steps.Length; i++)
        {
            Console.WriteLine($"       {walk.Steps[i],-11} Flux: {theirs[i]}");
            if (mine is not null && mine[i] != theirs[i])
            {
                Console.WriteLine($"       {"",-11} Rask: {mine[i]}");
            }
        }
    }

    // One dropdown of the page, walked. Each step waits on STATE — the panel open or closed for three frames in a row —
    // and then writes down what it left behind.
    private async Task<List<string>> WalkOne(string url, Walk walk, string prefix)
    {
        var context = await browser.NewContextAsync(view.Context());
        var page = await context.NewPageAsync();
        var finger = view.Phone ? await context.NewCDPSessionAsync(page) : null;
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 120000 });
        await page.Run("() => document.fonts.ready.then(() => undefined)");
        // Flux's page has links above its first example; a parity page begins with it, and Shift+Tab off the first
        // trigger would land on nothing.
        await page.Run("() => { document.body.prepend(Object.assign(document.createElement('button'), { style: 'position:absolute;opacity:0' })); }");
        var dropdown = (await page.QuerySelectorAllAsync($"[data-preview-wrapper] [data-{prefix}-dropdown]"))[walk.Dropdown];
        await dropdown.Run("el => el.scrollIntoView({ block: 'center' })");
        var trigger = (await dropdown.EvaluateHandleAsync("el => el.firstElementChild")).AsElement()!;
        var panel = (await dropdown.QuerySelectorAsync($"[data-{prefix}-popover]"))!;
        // A pointer comes from somewhere: Flux's page now and then does not open under a pointer whose first move ever
        // lands on the trigger. On a phone a finger has touched the page before (FIRST_TOUCH, above).
        if (view.Phone)
        {
            await Screen.Press(page, 4, 450, true);
        }
        else
        {
            await page.Mouse.MoveAsync(4, 450);
        }

        await page.WaitForTimeoutAsync(200);
        static async Task<double[]> Centre(IElementHandle el)
        {
            var box = (await el.BoundingBoxAsync())!;
            return [box.X + (box.Width / 2), box.Y + (box.Height / 2)];
        }

        var states = new List<string>();
        // Where the panel was when it was last open: a step that goes there says so on both pages alike.
        double[]? onPanel = null;
        foreach (var step in walk.Steps)
        {
            var scrolled = Js.Number(await page.Run("() => scrollY"));
            if (await panel.BoundingBoxAsync() is not null)
            {
                onPanel = await Centre(panel);
            }

            switch (step)
            {
                case "click":
                    var c = await Centre(trigger);
                    await Screen.Press(page, c[0], c[1], view.Phone);
                    break;
                case "hover":
                    var h = await Centre(trigger);
                    await page.Mouse.MoveAsync((float)h[0], (float)h[1]);
                    break;
                case "hoverpanel":
                    await page.Mouse.MoveAsync((float)onPanel![0], (float)onPanel[1]);
                    break;
                // Two moves with nothing between them: one that stays on the trigger, one into the gap under it.
                case "glide":
                    var g = await Centre(trigger);
                    await page.Mouse.MoveAsync((float)(g[0] + 6), (float)(g[1] + 10));
                    await page.Mouse.MoveAsync((float)(g[0] + 12), (float)(g[1] + 21));
                    break;
                case "gap":
                    var p = await Centre(trigger);
                    await page.Mouse.MoveAsync((float)(p[0] + 12), (float)(p[1] + 21));
                    break;
                case "clickpanel":
                    await Screen.Press(page, onPanel![0] - 80, onPanel[1], view.Phone);
                    break;
                case "outside":
                    await Screen.Press(page, 4, 450, view.Phone);
                    break;
                case "leave":
                    await page.Mouse.MoveAsync(4, 450);
                    break;
                case "focus":
                    await trigger.FocusAsync();
                    break;
                // A finger swipes the page 120 px up, where a wheel turns.
                case "wheel" when finger is not null:
                    await finger.SendAsync("Input.synthesizeScrollGesture", new Dictionary<string, object>
                    {
                        ["x"] = 195,
                        ["y"] = 600,
                        ["yDistance"] = -120,
                        ["gestureSourceType"] = "touch",
                        ["speed"] = 800,
                    });
                    break;
                case "wheel":
                    await page.Mouse.WheelAsync(0, 120);
                    break;
                default:
                    await page.Keyboard.PressAsync(step);
                    break;
            }

            await panel.Run(Settled);
            await page.WaitForTimeoutAsync(60);
            states.Add(Js.Str(await dropdown.Run(Read, new { prefix, scrolled })) ?? "");
        }

        await context.CloseAsync();
        return states;
    }
}
