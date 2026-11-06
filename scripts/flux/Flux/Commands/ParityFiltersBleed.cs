using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     Ui.Filters' list laid out as Flux's full-bleed table (fluxui.dev/components/table#full-bleed), measured side by side
///     at 1920 and 390: the card, its header row, the button on the right, the gap to the table, and the table bleeding
///     through the card's padding. What sits between the header and the table (the filter badges) is Rask's own and is
///     not compared; neither is the button's label.
/// </summary>
/// <remarks><code>flux parity-filters-bleed http://127.0.0.1:PORT</code> Exit code 1 on any difference.</remarks>
[FluxCommand("parity-filters-bleed", "Ui.Filters' list against Flux's full-bleed table, on a running site")]
public sealed class ParityFiltersBleed : IFluxCommand
{
    // Runs in the page. Every length relative to the card's own box, so the two pages' different surroundings do not count.
    private const string MeasureCard = """
        (card) => {
          const r = el => el.getBoundingClientRect();
          const c = r(card);
          const row = card.querySelector(':scope > div');
          const button = row.querySelector('button');
          const table = card.querySelector('table');
          const th = table.querySelector('th');
          const words = document.createRange();
          const walker = document.createTreeWalker(th, NodeFilter.SHOW_TEXT, { acceptNode: n => n.textContent.trim() ? 1 : 3 });
          words.selectNodeContents(walker.nextNode());
          const cs = getComputedStyle(card);
          return {
            cardPadding: [cs.paddingTop, cs.paddingRight, cs.paddingBottom, cs.paddingLeft].join(' '),
            cardRadius: cs.borderRadius,
            rowHeight: Math.round(r(row).height),
            rowAlign: getComputedStyle(row).alignItems + ' ' + getComputedStyle(row).justifyContent,
            buttonHeight: Math.round(r(button).height),
            buttonRightInset: Math.round(c.right - r(button).right),
            buttonTop: Math.round(r(button).top - c.top),
            tableLeft: Math.round(r(table).left - c.left),
            tableRightInset: Math.round(c.right - r(table).right),
            firstHeadingTextLeft: Math.round(words.getBoundingClientRect().left - c.left),
            headingToTableGap: Math.round(r(table).top - r(row).bottom),
          };
        }
        """;

    // Runs in the page. The count beside a label, as Flux draws one: its nav bar item's badge
    // (fluxui.dev/components/navbar#with-badges), which is Flux's only count set inside a control. Compared with the
    // count in the Filters button.
    private const string Chip = """
        ([label, chip]) => {
          const words = document.createRange();
          words.selectNodeContents(label);
          const s = getComputedStyle(chip);
          return {
            chipGap: Math.round(chip.getBoundingClientRect().left - words.getBoundingClientRect().right),
            chipHeight: Math.round(chip.getBoundingClientRect().height),
            chipPadding: [s.paddingTop, s.paddingRight, s.paddingBottom, s.paddingLeft].join(' '),
            chipRadius: s.borderRadius,
            chipFont: s.fontSize + ' ' + s.fontWeight,
            chipMinWidth: s.minWidth,
          };
        }
        """;

    // Runs in the page. The text node that says `words`, inside `root`.
    private const string TextNode = """
        (root, words) => {
          const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, { acceptNode: n => n.textContent.trim() === words ? 1 : 3 });
          return walker.nextNode();
        }
        """;

    // At a phone's width these follow the words, not the layout: Flux's subtitle wraps to three lines (a taller row,
    // the button centred lower) and its four wider columns scroll inside the card. They are printed, not judged.
    private static readonly Dictionary<int, string[]> Words = new() { [390] = ["rowHeight", "buttonTop", "tableRightInset"] };

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var ours = args.Length > 0 ? args[0] : "http://127.0.0.1:5050";
        var browser = await Browsers.Chromium();
        var failed = 0;
        foreach (var width in new[] { 1920, 390 })
        {
            var page = await browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = width, Height = 1000 } });
            var idle = new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle };

            await page.GotoAsync("https://fluxui.dev/components/table", idle);
            var flux = (JsonObject)(await page.Locator("[data-flux-card]", new PageLocatorOptions { HasText = "Recent customers" }).First.Run(MeasureCard))!;

            // Our list with no filter in force, so nothing stands between the header and the table, as in Flux's.
            await page.GotoAsync(ours + "/ui/filters/", idle);
            var card = page.Locator("[data-testid=\"ui-filters\"] [data-ui-card]").First;
            await card.Locator("tbody tr").First.WaitForAsync();
            var rask = (JsonObject)(await card.Run(MeasureCard))!;

            await page.GotoAsync("https://fluxui.dev/components/navbar", idle);
            Merge(flux, await page.Locator("[data-flux-navbar-items]", new PageLocatorOptions { HasText = "Inbox" }).First.Run(
                $"item => ({Chip})([({TextNode})(item, 'Inbox'), [...item.querySelectorAll('*')].find(e => e.textContent.trim() === '12' && e.children.length === 0)])"));

            await page.GotoAsync(ours + "/ui/filters/?driver=ada", idle);
            var button = page.Locator("[data-testid=\"ui-filters\"] [data-ui-modal-trigger] button").First;
            await button.Locator("span span").WaitForAsync();
            Merge(rask, await button.Run($"button => ({Chip})([({TextNode})(button, 'Filters'), button.querySelector('span span')])"));

            foreach (var key in flux.Select(p => p.Key).ToList())
            {
                var (theirs, mine) = (JsString.Of(flux, key), JsString.Of(rask, key));
                var same = theirs == mine;
                var words = Words.TryGetValue(width, out var keys) && keys.Contains(key);
                failed += same || words ? 0 : 1;
                Console.WriteLine($"{width} {(same ? "ok  " : words ? "text" : "DIFF")} {key,-22} flux {theirs}  rask {mine}");
            }

            await page.CloseAsync();
        }

        await browser.CloseAsync();
        return failed > 0 ? 1 : 0;
    }

    // Object.assign(into, from).
    private static void Merge(JsonObject into, JsonNode? from)
    {
        foreach (var (key, value) in (from as JsonObject ?? []).ToList())
        {
            into[key] = value?.DeepClone();
        }
    }
}
