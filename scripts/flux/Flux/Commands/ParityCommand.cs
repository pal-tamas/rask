using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     Holds Ui.Command to Flux UI's command palette ONCE IT IS USED — the half parity cannot see, since a palette as loaded
///     has nothing typed, nothing lit and its modal shut.
/// </summary>
/// <remarks>
///     First the look: parity compares the page <c>command-open</c> (CommandOpenParity) with Flux's page brought to the same
///     states — typed into, left with no match, opened as a modal and walked — by the <c>command-open</c> preparation, which
///     drives both pages the same way.
///     <para>
///         Then the behaviour: each walk below is done with real keys and a real pointer on Flux's live page and on the Rask
///         parity page (<c>command</c>, given the runtime's hooks, as an app has them), and after every step both write down
///         what they show — the commands shown, the lit one, what the box holds, whether the clear or close button and the
///         "No results found" row show, whether the modal is open, and where focus is. The two transcripts must agree line
///         for line. On the Rask page the box must also NAME the lit command (aria-activedescendant), as Flux's does.
///     </para>
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask pages
///     flux parity-command [--refresh] [--all]                       # --all prints every step of every walk
///     flux parity-command --record                                  # Flux's walks alone, every step
///     flux parity-command --view 390x844                            # the same on a phone: taps, and no pointer
///                                                                   # to rest on a command or take away
///     </code>
///     Exit code 1 on any difference.
/// </remarks>
[FluxCommand("parity-command", "a command palette in use against Flux's: its look, then its walks")]
public sealed class ParityCommand : IFluxCommand
{
    private const string Palette = "[data-flux-command], [data-ui-command]";

    // Runs in the page. What a step left behind, in words both pages can be held to.
    private const string Read = """
        function read({ example, palette, rask }) {
          const wrapper = document.querySelectorAll('[data-preview-wrapper]')[example];
          const dialog = wrapper.querySelector('dialog');
          const root = (dialog?.open ? dialog : wrapper).querySelector(palette);
          const box = root.querySelector('input');
          const words = el => (el.innerText ?? '').trim().split('\n')[0].trim();
          const seen = el => !!el && el.checkVisibility();
          const rows = [...root.querySelectorAll('[role=option]')].filter(seen);
          const lit = rows.find(row => row.hasAttribute('data-active'));
          const named = document.getElementById(box.getAttribute('aria-activedescendant') ?? '');
          const empty = [...root.querySelectorAll('[role=listbox] > :not([role=option])')].some(seen);
          const button = name => seen(root.querySelector(`button[aria-label^="${name}"]`));
          const focused = document.activeElement;
          const focus = focused === box ? 'box'
            : focused?.getAttribute('role') === 'listbox' && root.contains(focused) ? 'list'
              : wrapper.contains(focused) && focused.closest('[data-flux-modal-trigger], [data-ui-modal-trigger]') ? 'trigger'
                // Past the example: the next thing each page has there is the page's, not the palette's.
                : focused === document.body ? 'body' : 'elsewhere';
          return [
            `shows [${rows.map(words).join(', ')}]`,
            `lit ${lit ? `"${words(lit)}"` : '-'}`,
            rask && named !== (lit ?? null) ? `NAMES "${named ? words(named) : '-'}"` : '',
            `box "${box.value}"`,
            button('Clear') ? 'clear' : '',
            button('Close') ? 'close' : '',
            empty ? 'NO RESULTS' : '',
            dialog ? (dialog.open ? 'open' : 'shut') : '',
            `focus ${focus}`,
          ].filter(Boolean).join(' · ');
        }
        """;

    // Steps: `box` presses the search box, `trigger` the modal's trigger, `row:<n>` / `hover:<n>` the n-th command
    // SHOWN, `clear` / `close` the box's buttons, `leave` takes the pointer off the palette, `type:<text>`, or a key.
    private static readonly CommandWalk[] Walks =
    [
        new("keys", 0,
        [
            "box", "ArrowDown", "ArrowDown", "ArrowUp", "ArrowUp", "ArrowUp", "End", "Home", "type:c", "type:h", "type:zz",
            "ArrowDown", "Backspace", "Backspace", "Backspace", "Backspace", "ArrowDown", "ArrowDown", "ArrowDown", "ArrowDown",
            "ArrowDown", "ArrowDown", "type:DOC", "Enter", "Escape", "Tab",
        ]),
        new("pointer", 0, ["box", "hover:2", "ArrowDown", "leave", "hover:4", "ArrowUp", "row:1", "ArrowDown", "box", "type:doc", "row:0", "box", "Tab"]),
        new("clear", 0, ["box", "type:new", "clear", "type:x", "Backspace", "Tab"]),
        new("modal", 1, ["trigger", "Tab", "type:doc", "ArrowDown", "Enter", "type:set", "Escape", "trigger", "box", "type:x", "close", "trigger", "Escape"]),
        new("back by Tab", 1, ["trigger", "Tab", "type:set", "Escape", "trigger", "Tab", "type:x", "Shift+Tab", "Tab", "Escape"]),
    ];

    private sealed record CommandWalk(string Name, int Example, IReadOnlyList<string> Steps);

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var arg = new Args(args);
        var record = arg.Flag("record");
        var every = arg.Flag("all") || record;
        var view = View.Of(arg);
        var raskPage = Repo.Parity("rask", "command.html");
        if (!File.Exists(raskPage))
        {
            Console.Error.WriteLine($"flux parity: {raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages");
            return 1;
        }

        var look = await Subcommand.Run<Parity>(["command-open", "--prepare", "command-open", .. arg.All.Where(a => a is "--refresh" or "--all"),
            .. view.Sized.Length > 0 ? new[] { "--view", $"{view.Width}x{view.Height}" } : []]);

        var browser = await Browsers.Chromium();
        var differs = 0;
        Console.WriteLine("\nwalks (Flux | Rask)");
        foreach (var asked in Walks)
        {
            var walk = view.Phone ? asked with { Steps = asked.Steps.Where(step => !step.StartsWith("hover", StringComparison.Ordinal) && !step.StartsWith("leave", StringComparison.Ordinal)).ToList() } : asked;
            var theirs = await WalkOne(browser, view, "https://fluxui.dev/components/command", walk, false);
            var mine = record ? theirs : await WalkOne(browser, view, Repo.FileUrl(raskPage), walk, true);
            var off = walk.Steps.Where((_, i) => theirs[i] != mine[i]).Count();
            differs += off > 0 ? 1 : 0;
            Console.WriteLine($"{(off > 0 ? "FAIL" : "ok  ")} {walk.Name} — {walk.Steps.Count} steps{(off > 0 ? $", {off} differ" : "")}");
            for (var i = 0; i < walk.Steps.Count; i++)
            {
                if (!every && theirs[i] == mine[i])
                {
                    continue;
                }

                Console.WriteLine($"       {walk.Steps[i],-10} Flux: {theirs[i]}");
                if (theirs[i] != mine[i])
                {
                    Console.WriteLine($"       {"",-10} Rask: {mine[i]}");
                }
            }
        }

        await browser.CloseAsync();
        Console.WriteLine(differs > 0 ? $"\nflux parity: the command palette walks differently in {differs} walk(s)." : "\nflux parity: the command palette walks as Flux's does.");
        return look != 0 || differs > 0 ? 1 : 0;
    }

    private static async Task<List<string>> WalkOne(IBrowser browser, View view, string url, CommandWalk walk, bool rask)
    {
        var context = await browser.NewContextAsync(view.Context());
        var page = await context.NewPageAsync();
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 120000 });
        await page.Run("() => document.fonts.ready.then(() => undefined)");
        if (rask)
        {
            await Runtime.With(page, "rask-hooks.ts");
            await page.Run("() => { document.body.style.paddingBottom = '100vh'; }");
        }

        var scope = (await page.QuerySelectorAllAsync("[data-preview-wrapper]"))[walk.Example];
        await scope.Run("el => el.scrollIntoView({ block: 'center' })");
        // The page holds still under the keys. Home and End in an EMPTY box are the browser's, which scrolls the page —
        // on both palettes (measured: neither prevents them) — and fluxui.dev scrolls smoothly, sweeping its examples
        // under the pointer the walk left on the box: it crosses a command, lights it, leaves it and lights none, as a
        // palette should. That is the docs page's layout walked under the pointer, not the palette's keys, and the pointer
        // walk holds the palette to Flux's under a moving pointer. Script still scrolls (scrollIntoView, Playwright's own).
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = "html { overflow: hidden !important; }" });

        // The palette a step acts on: the open modal's, else the example's.
        async Task<IElementHandle> Current() => (await page.QuerySelectorAsync($"dialog[open] :is({Palette})")) ?? (await scope.QuerySelectorAsync(Palette))!;
        async Task<IReadOnlyList<IElementHandle>> ShownRows() => await (await Current()).QuerySelectorAllAsync(":is([data-flux-command-item], [data-ui-command-item]):not([data-hidden])");
        // A press is a tap on a phone.
        Task Press(IElementHandle? el) => view.Phone ? el!.TapAsync() : el!.ClickAsync();

        var lines = new List<string>();
        foreach (var step in walk.Steps)
        {
            var parts = step.Split(':');
            var (verb, value) = (parts[0], parts.Length > 1 ? parts[1] : "");
            switch (verb)
            {
                case "box":
                    await Press(await (await Current()).QuerySelectorAsync("input"));
                    break;
                case "trigger":
                    await Press(await scope.QuerySelectorAsync("[data-flux-modal-trigger] button, [data-ui-modal-trigger] button"));
                    break;
                case "row":
                    await Press((await ShownRows())[int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)]);
                    break;
                case "hover":
                    await (await ShownRows())[int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)].HoverAsync();
                    break;
                case "clear" or "close":
                    await Press(await (await Current()).QuerySelectorAsync($"button[aria-label^=\"{(verb == "clear" ? "Clear" : "Close")}\"]"));
                    break;
                case "leave":
                    await page.Mouse.MoveAsync(2, 2);
                    break;
                case "type":
                    await page.Keyboard.TypeAsync(value, new KeyboardTypeOptions { Delay = 60 });
                    break;
                default:
                    await page.Keyboard.PressAsync(verb);
                    break;
            }

            await page.Run("() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done)))");
            await page.WaitForTimeoutAsync(350);
            lines.Add(Js.Str(await page.Run(Read, new { example = walk.Example, palette = Palette, rask })) ?? "");
        }

        await context.CloseAsync();
        return lines;
    }
}
