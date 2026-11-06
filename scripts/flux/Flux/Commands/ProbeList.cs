using System.Globalization;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     Reads what Flux UI's live page leaves behind, step by step, in a control that opens a list: every attribute of the
///     control, its trigger, its popover and its rows, where focus is, and how many requests the step sent.
/// </summary>
/// <remarks>
///     The open-list checks compare a walk in words; this prints the markup those words are read from, which is how a
///     behaviour table is written down before the hook that follows it (rask-listbox.ts).
///     <code>
///     flux probe-list &lt;slug&gt; &lt;example index&gt; &lt;steps&gt; [--brief]
///     flux probe-list select 3 click,hover:2,ArrowDown,type:le,Escape
///     </code>
///     Steps: focus · click · click:&lt;row&gt; · hover:&lt;row&gt; · leave · outside · type:&lt;text&gt; · wait:&lt;ms&gt; ·
///     any key name. <c>--brief</c> prints one line a step — the rows shown, lit and picked, the active descendant, the
///     focus — and everything after the last step.
/// </remarks>
[FluxCommand("probe-list", "what Flux's list control leaves behind, step by step")]
public sealed class ProbeList : IFluxCommand
{
    // Runs in the page.
    private const string Read = """
        function read(wrapper) {
          const root = wrapper.querySelector('[data-flux-select],[data-flux-pillbox],[data-flux-autocomplete]') ?? wrapper;
          const attrs = el => [...el.attributes].filter(a => !/^(class|style|wire:|x-)/.test(a.name))
            .map(a => (a.value === '' ? a.name : `${a.name}=${a.value}`)).join(' ');
          const words = el => el.textContent.trim().replace(/\s+/g, ' ').slice(0, 24);
          const out = [`<${root.localName}> ${attrs(root)}`];
          for (const el of root.querySelectorAll('button[role=combobox], input, [data-flux-pillbox-trigger], [role=listbox], [popover], ui-selected')) {
            out.push(`  <${el.localName}> ${attrs(el)}${el.localName === 'input' ? ` value="${el.value}"` : ''}${el.localName === 'ui-selected' ? ` says "${words(el)}"` : ''}`);
          }

          for (const row of root.querySelectorAll('ui-option, ui-option-create, ui-option-empty, ui-empty, ui-option-group, [role=presentation]')) {
            out.push(`    <${row.localName}> "${words(row)}" ${attrs(row)} display=${getComputedStyle(row).display}`);
          }

          const focused = document.activeElement;
          const popup = root.querySelector('[popover]');
          const list = root.querySelector('[role=listbox]');
          out.push(`  FOCUS <${focused.localName}> ${attrs(focused)} · open=${popup ? popup.matches(':popover-open') : '?'} · scrollTop=${list ? list.scrollTop : '-'}`);
          return out.join('\n');
        }
        """;

    // Runs in the page. The same, in one line.
    private const string Line = """
        function line(wrapper) {
          const root = wrapper.querySelector('[data-flux-select],[data-flux-pillbox],[data-flux-autocomplete]') ?? wrapper;
          const words = el => el.textContent.trim().replace(/\s+/g, ' ').slice(0, 24);
          const rows = [...root.querySelectorAll('ui-option, ui-option-create, ui-option-empty, ui-empty')].filter(row => getComputedStyle(row).display !== 'none');
          const named = [...root.querySelectorAll('[aria-activedescendant]')].map(el => `${el.localName}→"${words(document.getElementById(el.getAttribute('aria-activedescendant')) ?? el)}"`);
          const focused = document.activeElement;
          const popup = root.querySelector('[popover]');
          const input = root.querySelector('input:not([type=hidden])');
          return [
            popup?.matches(':popover-open') ? 'open' : 'closed',
            `lit [${rows.filter(row => row.hasAttribute('data-active')).map(words).join(', ')}]`,
            `picked [${rows.filter(row => row.getAttribute('aria-selected') === 'true').map(words).join(', ')}]`,
            `shows ${rows.length}${rows.length < 4 ? ` [${rows.map(row => `${row.localName}:${words(row)}`).join(', ')}]` : ''}`,
            `names ${named.join(' ') || '-'}`,
            `focus <${focused.localName}${focused.getAttribute('role') ? ` role=${focused.getAttribute('role')}` : ''}>`,
            input ? `input "${input.value}"` : '',
            `says "${words(root.querySelector('ui-selected') ?? root)}"`,
          ].filter(Boolean).join(' · ');
        }
        """;

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var slug = args.Length > 0 ? args[0] : "select";
        var example = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 3;
        var walk = args.Length > 2 ? args[2] : "click";
        var brief = args.Contains("--brief");
        var steps = walk.Split(',');
        var browser = await Browsers.Chromium();
        var context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1280, Height = 900 } });
        var page = await context.NewPageAsync();
        var requests = 0;
        page.Request += (_, request) =>
        {
            if (request.ResourceType is "xhr" or "fetch")
            {
                Interlocked.Increment(ref requests);
            }
        };
        await page.GotoAsync($"https://fluxui.dev/components/{slug}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 120000 });
        var scope = (await page.QuerySelectorAllAsync("[data-preview-wrapper]"))[example];
        await scope.Run("el => el.scrollIntoView({ block: 'center' })");
        var trigger = (await scope.QuerySelectorAsync("button[role=combobox], input[role=combobox], [data-flux-pillbox-trigger]"))!;
        Task<IReadOnlyList<IElementHandle>> Rows() => scope.QuerySelectorAllAsync("ui-option, ui-option-create");

        for (var at = 0; at < steps.Length; at++)
        {
            var step = steps[at];
            var split = step.Split(':');
            var (verb, arg) = (split[0], split.Length > 1 ? split[1] : null);
            var before = requests;
            switch (verb)
            {
                case "focus": await trigger.FocusAsync(); break;
                case "click" when arg is null: await trigger.ClickAsync(); break;
                case "click": await (await Rows())[int.Parse(arg!, CultureInfo.InvariantCulture)].ClickAsync(); break;
                case "hover": await (await Rows())[int.Parse(arg!, CultureInfo.InvariantCulture)].HoverAsync(); break;
                case "leave": await page.Mouse.MoveAsync(2, 2); break;
                case "outside": await page.Mouse.ClickAsync(5, 5); break;
                case "type": await page.Keyboard.TypeAsync(arg ?? "", new KeyboardTypeOptions { Delay = 60 }); break;
                case "wait": await page.WaitForTimeoutAsync(int.Parse(arg!, CultureInfo.InvariantCulture)); break;
                default: await page.Keyboard.PressAsync(verb); break;
            }

            await page.WaitForTimeoutAsync(350);
            var state = Js.Str(await scope.Run(Read));
            Console.WriteLine($"=== {step} (requests +{requests - before})");
            Console.WriteLine(brief && at < steps.Length - 1 ? Js.Str(await scope.Run(Line)) : state);
        }

        Console.WriteLine($"requests in all: {requests}");
        await browser.CloseAsync();
        return 0;
    }
}
