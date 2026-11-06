using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     Holds every clear button of the kit to the box it clears, in the containers an app lays a form out with.
/// </summary>
/// <remarks>
///     The page is ClearPlacementParity's (artifacts/flux-parity/rask/clear-placement.html): each control with a value and
///     <c>Clearable</c>, alone, labelled, in a row beside a date picker, grown, in a grid cell and in a column, at 1920
///     and at 390, in light and in dark. For each one the button must lie inside its trigger's box, centred on it top to
///     bottom; be what a press at its centre lands on; stand the same distance from the trigger's end in every container.
///     Flux's public pages draw one clear button, the input's. It is measured on fluxui.dev and the kit's input is held
///     to it: its size and where it stands in the box. An input a row stretches is left as Flux's is (Stretched).
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the page
///     flux clear-placement                        # every case
///     flux clear-placement --offline              # without Flux's page
///     flux clear-placement --shots &lt;dir&gt;          # and a PNG per width and scheme
///     </code>
///     Exit code 1 on any difference.
/// </remarks>
[FluxCommand("clear-placement", "every clear button of the kit against the box it clears")]
public sealed class ClearPlacement : IFluxCommand
{
    private static readonly int[] Widths = [1920, 390];

    // In the page: every case's button against its trigger. A trigger is the bordered box (`data-*-group-target`),
    // or the control itself where it has none.
    private const string Read = """
        () => {
          const box = el => { const r = el.getBoundingClientRect(); return { x: r.x, y: r.y, right: r.right, bottom: r.bottom, w: r.width, h: r.height }; };
          const shown = el => el.getClientRects().length > 0 && getComputedStyle(el).visibility !== 'hidden';
          return [...document.querySelectorAll('[data-clear-case]')].map(host => {
            const name = `${host.dataset.control} · ${host.dataset.clearCase}`;
            const clear = [...host.querySelectorAll('[aria-label^="Clear"]')].filter(shown).at(-1);
            if (!clear) return { name, missing: true };
            // A press is asked of the viewport, so the button is brought into it first.
            clear.scrollIntoView({ block: 'center', behavior: 'instant' });
            const control = clear.closest('[data-ui-control], [data-flux-control], [data-ui-input], [data-flux-input]') ?? host;
            const trigger = [...control.querySelectorAll('[data-ui-group-target], [data-flux-group-target]')].find(shown) ?? control;
            const c = box(clear);
            const hit = document.elementFromPoint(c.x + c.w / 2, c.y + c.h / 2);
            return { name, control: host.dataset.control, where: host.dataset.clearCase, clear: c, trigger: box(trigger), hit: clear.contains(hit), position: getComputedStyle(clear).position,
              inTrigger: trigger !== clear && trigger.contains(clear), tab: clear.tabIndex };
          });
        }
        """;

    // The input is Flux's to the pixel, and Flux places its buttons against the input's whole room: where a row or a
    // grid cell stretches that room, Flux's own clear button drifts down with it (measured: 40px beside a 120px
    // neighbour). The kit's does the same, so there the input is held to a press and to its distance from the end.
    private static readonly string[] Stretched = ["beside a labelled date picker", "grown in a row", "in a grid cell"];

    // Flux's pickers draw their × INSIDE the trigger, between the choice and the chevron, out of the Tab order
    // (measured on the datetime picker, 2026-10-26: a 32px div, 29px from the trigger's end). The kit's pickers and its
    // listbox are held to that; Flux shows no clearable date picker, time picker or listbox, so its datetime picker's
    // place is theirs (a typed field ends the same way).
    private static readonly string[] InTrigger = ["date-picker", "date-picker-typed", "date-picker-range", "time-picker", "time-picker-typed", "select"];
    private const double FluxEnd = 29;

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var arg = new Args(args);
        var shots = arg.Option("shots");
        var file = Repo.Parity("rask", "clear-placement.html");
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"clear placement: {file} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages");
            return 1;
        }

        var browser = await Browsers.Chromium();
        var failed = 0;
        foreach (var width in Widths)
        {
            foreach (var scheme in new[] { "light", "dark" })
            {
                var context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = new ViewportSize { Width = width, Height = 900 },
                    ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
                });
                var page = await context.NewPageAsync();
                await page.GotoAsync(Repo.FileUrl(file), new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                await page.Run("() => document.fonts.ready.then(() => undefined)");
                var found = (await page.Run(Read) as JsonArray ?? []).OfType<JsonObject>().ToList();
                var (wrong, ends) = Judge(found);
                if (shots is not null)
                {
                    Directory.CreateDirectory(shots);
                    await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(shots, $"clear-placement-{width}-{scheme}.png"), FullPage = true });
                    // The row an app reported, whole: two pickers are 480px at their narrowest, wider than a phone.
                    foreach (var control in new[] { "time-picker", "date-picker" })
                    {
                        var clip = (await page.Run("""
                            name => {
                              const row = document.querySelector(`[data-control="${name}"][data-clear-case="beside a date picker"]`);
                              const box = row.getBoundingClientRect();
                              return { x: box.x + scrollX, y: box.y + scrollY - 8, width: row.scrollWidth, height: box.height + 16 };
                            }
                            """, control))!;
                        await page.ScreenshotAsync(new PageScreenshotOptions
                        {
                            Path = Path.Combine(shots, $"row-{control}-{width}-{scheme}.png"),
                            FullPage = true,
                            Clip = new Clip { X = (float)D(clip["x"]), Y = (float)D(clip["y"]), Width = (float)D(clip["width"]), Height = (float)D(clip["height"]) },
                        });
                    }
                }

                if (scheme == "light" && !arg.Flag("offline"))
                {
                    var flux = await FluxInput(browser, width);
                    var mine = found.FirstOrDefault(one => Js.Str(one["name"]) == "input · alone");
                    if (flux is null)
                    {
                        wrong.Add("Flux's input page draws no clear button any more: measure again");
                    }
                    else if (mine is not null && !Js.Truthy(mine["missing"]))
                    {
                        var (c, t) = (mine["clear"]!, mine["trigger"]!);
                        var kit = new Dictionary<string, double>(StringComparer.Ordinal)
                        {
                            ["w"] = D(c["w"]),
                            ["h"] = D(c["h"]),
                            ["top"] = D(c["y"]) - D(t["y"]),
                            ["end"] = D(t["right"]) - D(c["right"]),
                        };
                        foreach (var key in new[] { "w", "h", "top", "end" })
                        {
                            if (!Near(kit[key], D(flux[key])))
                            {
                                wrong.Add($"input · alone: {key} is {Js.Num(kit[key])} and Flux's is {Js.Num(D(flux[key]))}");
                            }
                        }

                        Console.WriteLine($"  Flux's input at {width}: {Js.Num(D(flux["w"]))}×{Js.Num(D(flux["h"]))}, {Js.Num(D(flux["top"]))}px from the top, {Js.Num(D(flux["end"]))}px from the end — the kit's: {Js.Num(kit["w"])}×{Js.Num(kit["h"])}, {Js.Num(kit["top"])}, {Js.Num(kit["end"])}");
                    }
                }

                Console.WriteLine($"{width} {scheme}: {found.Count} cases, {wrong.Count} wrong"
                    + (scheme == "light" ? $" — from the trigger's end: {string.Join(", ", ends.Select(e => $"{e.Control} {Js.Num(e.End)}"))}" : ""));
                foreach (var line in wrong)
                {
                    Console.WriteLine($"  FAIL {line}");
                }

                failed += wrong.Count;
                await context.CloseAsync();
            }
        }

        await browser.CloseAsync();
        Console.WriteLine(failed == 0 ? "clear placement: every button is in its trigger" : $"clear placement: {failed} wrong");
        return failed == 0 ? 0 : 1;
    }

    private static double D(JsonNode? value) => Js.Number(value);

    private static bool Near(double a, double b) => Math.Abs(a - b) <= 0.51;

    // `x.toFixed(n)`, for the few numbers printed that way.
    private static string Fixed(double value, int digits) => value.ToString($"F{digits}", CultureInfo.InvariantCulture);

    private static (List<string> Wrong, List<(string Control, double End, string Name)> Ends) Judge(List<JsonObject> found)
    {
        var wrong = new List<string>();
        var ends = new List<(string Control, double End, string Name)>();
        foreach (var one in found)
        {
            var name = Js.Str(one["name"]);
            if (Js.Truthy(one["missing"]))
            {
                wrong.Add($"{name}: no clear button is shown");
                continue;
            }

            var (c, t) = (one["clear"]!, one["trigger"]!);
            var control = Js.Str(one["control"]) ?? "undefined";
            var inside = D(c["x"]) >= D(t["x"]) - 0.5 && D(c["right"]) <= D(t["right"]) + 0.5 && D(c["y"]) >= D(t["y"]) - 0.5 && D(c["bottom"]) <= D(t["bottom"]) + 0.5;
            var asFlux = control == "input" && Stretched.Contains(Js.Str(one["where"]));
            if (asFlux)
            {
                // drifts as Flux's does
            }
            else if (!inside)
            {
                wrong.Add($"{name}: the button is outside its trigger — {Js.Num(Js.Round(D(c["y"]) - D(t["y"])))}px from its top, {Js.Num(Js.Round(D(t["right"]) - D(c["right"])))}px from its end (trigger {Js.Num(Js.Round(D(t["w"])))}×{Js.Num(Js.Round(D(t["h"])))})");
            }
            else if (!Near(D(c["y"]) + D(c["h"]) / 2, D(t["y"]) + D(t["h"]) / 2))
            {
                wrong.Add($"{name}: the button is {Fixed(D(c["y"]) + D(c["h"]) / 2 - D(t["y"]) - D(t["h"]) / 2, 1)}px off the trigger's middle");
            }

            if (!Js.Truthy(one["hit"]))
            {
                wrong.Add($"{name}: a press at the button's centre lands on something else");
            }

            if (InTrigger.Contains(control))
            {
                if (!Js.Truthy(one["inTrigger"]))
                {
                    wrong.Add($"{name}: the × is not inside its trigger");
                }

                if (D(one["tab"]) != -1)
                {
                    wrong.Add($"{name}: the × is in the Tab order (tabIndex {Js.Num(D(one["tab"]))})");
                }

                if (!Near(D(t["right"]) - D(c["right"]), FluxEnd))
                {
                    wrong.Add($"{name}: the × is {Fixed(D(t["right"]) - D(c["right"]), 1)}px from the trigger's end, Flux's {Js.Num(FluxEnd)}");
                }
            }

            var end = Js.Parse(Fixed(D(t["right"]) - D(c["right"]), 2));
            var first = ends.FindIndex(e => e.Control == control);
            if (first < 0)
            {
                ends.Add((control, end, name ?? ""));
            }
            else if (!Near(ends[first].End, end))
            {
                wrong.Add($"{name}: {Js.Num(end)}px from the trigger's end, {Js.Num(ends[first].End)}px in \"{ends[first].Name}\"");
            }
        }

        return (wrong, ends);
    }

    // Flux's one drawn clear button: where it stands in the input's box.
    private static async Task<JsonNode?> FluxInput(IBrowser browser, int width)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = width, Height = 900 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync("https://fluxui.dev/components/input", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
        var found = await page.Run("""
            () => {
              const clear = document.querySelector('[data-preview-wrapper] [data-flux-input] button[aria-label^="Clear"]');
              if (!clear) return null;
              const input = clear.closest('[data-flux-input]').querySelector('input').getBoundingClientRect();
              const c = clear.getBoundingClientRect();
              return { w: c.width, h: c.height, top: c.y - input.y, end: input.right - c.right };
            }
            """);
        await context.CloseAsync();
        return found;
    }
}
