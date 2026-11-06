using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Flux.Preparations;

namespace Rask.Flux.Commands;

/// <summary>
///     Holds the OPEN date picker and time picker to Flux UI's, example by example.
/// </summary>
/// <remarks>
///     Parity measures a page as it loads, and a picker loads closed: its calendar or its list of times is in the tree and
///     not displayed. The popup is most of the component, so this measures Flux's page again with each picker open —
///     <see cref="Measure" /> opens whatever carries <c>data-parity-open</c>, by a real press at its centre, and closes it
///     again before the next — and files the result as the page <c>&lt;slug&gt;-open</c>, under the same section ids.
///     DatePickerOpenParity and TimePickerOpenParity write the Rask pages of those names with the same mark, and parity
///     compares the two as it compares any page: the popup's box, look and contents, and where it sits against the trigger
///     it belongs to.
///     <para>
///         THE DATE PICKER IS ANOTHER THING ON A PHONE. Under 640 px Flux's is a sheet centred in the viewport over a page
///         dimmed 10%: one month whatever <c>months</c> says, days 44 px, the presets a native select under them, and Cancel
///         and "Select date" at the foot — a pick marks the day and writes nothing until that button is pressed. So the date
///         picker is measured twice more at 390 × 844, as it loads and open, against the SAME two Rask pages (one markup;
///         the stylesheet decides), and then WALKED there: <c>--live &lt;url&gt;</c> names a running site whose page holds
///         a <c>Ui.DatePicker</c> with nothing chosen (<c>--picker &lt;css&gt;</c> says which; the first on the page unless
///         given), and every step of the walk has to read the same on Flux's first example and on it.
///     </para>
///     <para>
///         Then WHERE IT LANDS with no room: the first pickers of the page opened with their trigger held at the edges of
///         the viewport, and with more in the popup than fits (<see cref="Placement" />). The time picker at a phone's width
///         and a desktop's; the date picker at a phone's too, where the sheet is in the middle wherever the trigger is.
///     </para>
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages          # writes the Rask pages
///     flux parity-date date-picker [--refresh] [--all]                   # --refresh re-measures Flux
///     flux parity-date time-picker --section interval
///     flux parity-date date-picker --live http://localhost:5210/ui/date-picker/
///     </code>
/// </remarks>
[FluxCommand("parity-date", "the open date, time and datetime pickers against Flux's, and the phone walk")]
public sealed class ParityDate : IFluxCommand
{
    private static readonly int[] PhoneView = [390, 844];

    // Runs in the page, on a picker of either kit.
    private const string Read = """
        function read(picker) {
          const dialog = picker.querySelector('dialog');
          const open = dialog.matches(':popover-open') || dialog.open;
          const box = dialog.getBoundingClientRect();
          const trigger = picker.querySelector('button[role=combobox]').textContent.replace(/\s+/g, ' ').trim();
          const marked = [...dialog.querySelectorAll('td[data-selected]')].map(cell => cell.textContent.trim()).join(',') || 'none';
          const root = getComputedStyle(document.documentElement);
          const foot = [...dialog.querySelectorAll('button')].filter(b => b.getClientRects().length && /Cancel|Select/.test(b.textContent)).map(b => b.textContent.trim()).join(' / ');
          return [
            open ? `open at ${Math.round(box.x)},${Math.round(box.y)} ${Math.round(box.width)}×${Math.round(box.height)}` : 'closed',
            `trigger "${trigger}"`, `marked ${marked}`, open ? `foot "${foot}"` : '',
            `page ${root.overflow === 'hidden' && root.pointerEvents === 'none' ? 'held' : 'free'}`,
          ].filter(Boolean).join(' · ');
        }
        """;

    private sealed record WalkSide(string Name, string Url, string Picker, double Wait);

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var arg = new Args(args);
        var slug = arg.All.FirstOrDefault(a => a is "date-picker" or "time-picker" or "datetime-picker");
        if (slug is null)
        {
            Console.Error.WriteLine("usage: flux parity-date <date-picker|time-picker|datetime-picker> [--refresh] [--section <id>] [--all] [--live <url> [--picker <css>] [--wait <ms>]]");
            return 1;
        }

        // The datetime picker's popup is the date picker's dialog, a sheet on a phone as well.
        var dated = slug != "time-picker";
        var refresh = arg.Flag("refresh");
        // The desktop every page is measured at, and a phone for the picker that is another control there.
        int[]?[] views = dated ? [null, PhoneView] : [null];

        var output = Repo.Parity("flux", $"{slug}-open");
        foreach (var view in views)
        {
            var file = Path.Combine(output, $"measurements{Sized(view)}.json");
            if (File.Exists(file) && !refresh)
            {
                continue;
            }

            var browser = await Browsers.Chromium(new PickerOpen().Launch);
            var open = await Measure.MeasurePage(browser, $"https://fluxui.dev/components/{slug}", output + Sized(view), (page, _) => page.Run(PickerOpen.Mark, slug), view);
            await browser.CloseAsync();
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(file, Js.Stringify(open));
            Console.WriteLine($"flux: {(open["light"] as JsonArray)?.Count} {slug} example(s) measured open{(view is not null ? $" at {view[0]} × {view[1]}" : "")}");
        }

        // Flux's date popup is a <dialog> it opens itself, its list of times a popover; the kit's are both popovers.
        var popup = dated ? "dialog" : "[popover]";
        // A browser that SHOWS its scrollbars, as a reader's does (parity-modal). Headless hides them, and there the page
        // lock Flux's dialog puts on (`scrollbar-gutter: stable`) reserves 15px that no scrollbar takes: the dialog's
        // containing block is 625px of a 640px viewport (measured 2026-10-28), so from `sm` up its width cap and its far
        // edge sit 15px in, where no reader sees them. The kit keeps a gutter only where a scrollbar takes room (rask-lock.ts).
        var edges = await Browsers.Chromium(new PickerOpen().Launch);
        var astray = await Placement.Landings(edges, new Placement.Family($"{slug}-open",
            new Placement.Side($"https://fluxui.dev/components/{slug}", $"[data-preview-wrapper] [data-flux-{slug}]", popup, "[data-parity-open]",
                Prepare: page => page.Run(PickerOpen.Mark, slug)),
            new Placement.Side(Repo.FileUrl(Repo.Parity("rask", $"{slug}-open.html")), $"[data-preview-wrapper] [data-ui-{slug}]", popup, "[data-parity-open]"),
            [new(0), new(1), new(2)])
        {
            Refresh = refresh,
            All = arg.Flag("all"),
            Width = 260,
            Scrollbars = "shown",
            Views = dated ? [PhoneView, [640, 844], [1920, 1080]] : null,
        });
        Console.WriteLine(astray > 0 ? $"\nflux parity: the {slug}'s popup lands elsewhere for {astray} picker(s).\n" : $"\nflux parity: the {slug}'s popup lands where Flux's does.\n");

        var strayed = arg.Option("live") is { } live && slug == "date-picker" ? await Walk(edges, live, arg) : 0;
        await edges.CloseAsync();

        var passed = arg.All.Where((a, i) => a is "--all" or "--section" || (i > 0 && arg.All[i - 1] == "--section")).ToArray();
        var failed = astray + strayed;
        foreach (var view in views)
        {
            if (view is not null)
            {
                Console.WriteLine($"\n— at {view[0]} × {view[1]} —");
            }

            // Open, the Rask page is given the runtime's hooks, which place its popup as Flux's script places Flux's.
            failed += await Subcommand.Run<Parity>([$"{slug}-open", .. ViewArgs(view), .. passed, "--prepare", "picker-open"]);
            // As it loads at a phone's width too: the trigger is another size there, and so is what the closed popup holds.
            if (view is not null)
            {
                failed += await Subcommand.Run<Parity>([slug, .. ViewArgs(view), .. passed, .. refresh ? new[] { "--refresh" } : []]);
            }
        }

        return failed > 0 ? 1 : 0;
    }

    private static string Sized(int[]? view) => view is null ? "" : $"@{view[0]}x{view[1]}";

    private static string[] ViewArgs(int[]? view) => view is null ? [] : ["--view", $"{view[0]}x{view[1]}"];

    // ----- The walk on a phone -------------------------------------------------------------------------------
    // One scenario, driven with a real pointer and real keys on Flux's first example and on a picker of a running
    // site: what each step leaves — whether the sheet is open, what the trigger reads, which day is marked, whether
    // the page behind is held — has to be the same line on both.
    private static async Task<int> Walk(IBrowser browser, string live, Args arg)
    {
        WalkSide[] sides =
        [
            new("flux", "https://fluxui.dev/components/date-picker", "[data-preview-wrapper] [data-flux-date-picker]", 350),
            new("rask", live, arg.Option("picker") ?? "[data-ui-date-picker]", double.Parse(arg.Option("wait") ?? "700", CultureInfo.InvariantCulture)),
        ];
        var transcripts = new List<List<string>>();
        foreach (var side in sides)
        {
            var context = await browser.NewContextAsync(Screen.Of(PhoneView[0], PhoneView[1]));
            await context.Clock.SetFixedTimeAsync("2026-01-15T12:00:00Z");
            var page = await context.NewPageAsync();
            await page.GotoAsync(side.Url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 120000 });
            await page.Run("() => document.fonts.ready.then(() => undefined)");
            await page.WaitForTimeoutAsync((float)(side.Wait * 2));
            var picker = page.Locator(side.Picker).First;
            await picker.Run("el => el.scrollIntoView({ block: 'center' })");
            var lines = new List<string>();

            async Task Say(string step)
            {
                await page.WaitForTimeoutAsync((float)side.Wait);
                lines.Add($"{step}: {Js.Str(await picker.Run(Read))}");
            }

            async Task Open()
            {
                var box = (await picker.BoundingBoxAsync())!;
                await page.Mouse.ClickAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2));
            }

            Task Day(int number) => picker.Locator("dialog td:not([data-outside]) button")
                .Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{number}\s*$") }).First.ClickAsync();
            Task Press(string text) => picker.Locator("dialog button").Filter(new LocatorFilterOptions { HasText = text }).First.ClickAsync();

            await Say("as loaded");
            await Open();
            await Say("opened");
            await Day(20);
            await Say("picked the 20th");
            await Day(22);
            await Say("picked the 22nd");
            await Press("Cancel");
            await Say("Cancel");
            await Open();
            await Say("opened again");
            await Day(20);
            await Say("picked the 20th");
            await Press("Select date");
            await Say("Select date");
            await Open();
            await Say("opened a third time");
            await Day(9);
            await Say("picked the 9th");
            await page.Keyboard.PressAsync("Escape");
            await Say("Escape");
            await Open();
            await Say("opened a fourth time");
            await Day(12);
            await Say("picked the 12th");
            await page.Mouse.ClickAsync(8, 30);
            await Say("a press on the dimmed page");
            await Open();
            await Say("opened a fifth time");
            await Press("Select date");
            await Say("Select date, the 12th still marked");
            transcripts.Add(lines);
            await context.CloseAsync();
        }

        var differing = 0;
        for (var i = 0; i < transcripts[0].Count; i++)
        {
            var theirs = transcripts[0][i];
            var mine = i < transcripts[1].Count ? transcripts[1][i] : "undefined";
            var same = theirs == mine;
            differing += same ? 0 : 1;
            Console.WriteLine($"{(same ? "ok  " : "FAIL")} walk at 390 — {theirs}{(same ? "" : $"\n       vs {mine}")}");
        }

        Console.WriteLine(differing > 0 ? $"\nflux parity: the date picker's phone walk differs in {differing} step(s).\n" : "\nflux parity: the date picker walks on a phone as Flux's does.\n");
        return differing;
    }
}
