using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     Holds Ui.Editor IN USE to Flux's live editor: what parity cannot see, because it only exists once somebody types,
///     presses a toolbar button or opens a list.
/// </summary>
/// <remarks>
///     One scenario is driven on both pages with real keys and a real pointer — fluxui.dev/components/editor, and the page
///     FluxParityPages wrote (artifacts/flux-parity/rask/editor.html) with the kit's engine (src/Rask.Ui/Resources/ui-editor.js)
///     mounted on it — and each step writes down what it observes: the HTML value, the events the editor raised, a control's
///     states, where a popover opened, what has focus. The two transcripts must be the same, line for line, in light and in
///     dark: the look of every kind of node a document can hold (headings, lists, quote, code, links, rule…); the
///     placeholder, the empty value, the disabled editor; every toolbar toggle, by button and by shortcut, and the pressed
///     state it shows; the heading and align lists: where they open, the active option by key and by pointer, picking; the
///     link panel: where it opens, Enter, the insert and unlink buttons, ⌘K, Escape; the toolbar's arrow keys (they wrap)
///     and its single tab stop; undo and redo; Markdown input rules; the marks no example shows a button for (code,
///     subscript, superscript, highlight), by a control of that name.
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask page
///     flux parity-editor [--all] [--view 390x844]
///     </code>
///     On a phone (<c>--view 390x844</c>) every press is a tap; the keys still apply, for a phone has a keyboard on its
///     screen. A phone has no pointer to hover with, so the step that hovers an option is skipped there. Exit code 1 on any
///     difference.
/// </remarks>
[FluxCommand("parity-editor", "an editor in use — typing, toolbar, lists, link panel — against Flux's")]
public sealed class ParityEditor : IFluxCommand
{
    private const string Document = "<h1>One</h1><h2>Two</h2><h3>Three</h3><p>Para <strong>b</strong> <em>i</em> <s>s</s> <u>u</u> <code>c</code> <mark>m</mark> x<sub>sub</sub> y<sup>sup</sup> <a href=\"https://a.b\">link</a></p>"
        + "<ul><li><p>a</p><ul><li><p>nested</p></li></ul></li><li><p>b</p></li></ul><ol><li><p>one</p></li><li><p>two</p></li></ol>"
        + "<blockquote><p>quote</p><p>quote2</p></blockquote><pre><code class=\"language-js\">let a = 1;</code></pre><p style=\"text-align:center\">centered</p><p>last</p>";

    // Runs in the page. How one node of a document computes — what a reader of it would see.
    private const string Look = """
        function look(element, pseudo) {
          const c = getComputedStyle(element, pseudo);
          const keys = ['display', 'position', 'paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft', 'marginTop', 'marginBottom', 'marginLeft',
            'fontSize', 'lineHeight', 'fontWeight', 'fontStyle', 'color', 'backgroundColor', 'borderTopWidth', 'borderLeftWidth',
            'borderTopLeftRadius', 'textDecorationLine', 'listStyleType', 'minHeight', 'maxHeight', 'overflowY', 'whiteSpace', 'textAlign',
            'verticalAlign', 'outlineStyle', 'float', 'pointerEvents', 'cursor', 'overflowWrap', 'width'];
          const family = c.fontFamily.includes('mono') ? 'mono' : 'text';
          // A width to a tenth of a pixel: layout snaps to 1/64px, and where it snaps is not the component's.
          const said = key => (key === 'width' && c[key].endsWith('px') ? `${Math.round(parseFloat(c[key]) * 10) / 10}px` : c[key]);
          // The hand over a pressable node is the kit's owner-approved departure (Pressables.CursorDepartedLine): said, so the
          // line pair can be let go on it and nothing else.
          const pressed = c.cursor === 'pointer' && element.closest(window.__pressable) ? ';pressable' : '';
          return keys.map(key => `${key}=${said(key)}`).join(';') + `;family=${family}` + (pseudo ? `;content=${c.content}` : '') + pressed;
        }
        """;

    // Runs in the page. The states a toolbar control states, under names both pages use.
    private const string States = """
        function states(control) {
          const c = getComputedStyle(control);
          const said = ['aria-pressed', 'aria-expanded', 'tabindex'].filter(name => control.hasAttribute(name)).map(name => `${name}=${control.getAttribute(name)}`);
          for (const flag of ['data-match', 'data-open', 'aria-activedescendant', 'disabled']) if (control.hasAttribute(flag)) said.push(flag);
          return `[${said.join(' ')}] ${c.color} on ${c.backgroundColor}`;
        }
        """;

    // Runs before the page's own script: what focus, the keys and the popovers do, with the time of each.
    private const string Trace = """
        (() => {
          window.__trace = [];
          const name = el => el?.dataset ? `${el.dataset.slot ?? el.dataset.editor ?? el.closest('[data-editor]')?.dataset.editor ?? el.tagName}${el.hasAttribute('popover') ? '[popover]' : ''}` : String(el?.nodeName);
          for (const type of ['keydown', 'focusin', 'focusout', 'beforetoggle', 'toggle']) {
            document.addEventListener(type, event => window.__trace.push([Date.now(),
              `${type} ${name(event.target)}${event.key ? ` ${event.key}` : ''}${event.newState ? ` ${event.newState}` : ''} focus=${name(document.activeElement)}`]), true);
          }
        })();
        """;

    // Runs in the page. The Rask page: the surroundings Flux's example sits in, then the engine, as an app would have loaded it.
    private const string Mount = """
        async ({ engine, inherited }) => {
          const wrapper = document.querySelector('[data-preview-wrapper]');
          for (const [key, value] of Object.entries(inherited)) wrapper.style[key] = value;
          const module = await import(URL.createObjectURL(new Blob([engine], { type: 'text/javascript' })));
          for (const editor of document.querySelectorAll('[data-ui-editor]')) module.mount(editor);
          window.__engine = module;
        }
        """;

    // Runs in the page, on the content: every kind of node, as it computes.
    private const string Nodes = """
        c => {
          const seen = new Set(), out = [];
          for (const node of c.querySelectorAll('*')) {
            const parent = node.parentElement, own = parent === c ? '' : `${parent.tagName.toLowerCase()}>`;
            const key = `${own}${node.tagName.toLowerCase()}${node === parent.firstElementChild ? ':first' : ''}${node === parent.lastElementChild ? ':last' : ''}`;
            if (seen.has(key)) continue;
            seen.add(key);
            out.push(`${key} ${window.__look(node)}`);
            for (const pseudo of ['::before', '::after', '::marker']) {
              const drawn = getComputedStyle(node, pseudo).content;
              if (pseudo === '::marker' ? node.tagName === 'LI' : drawn !== 'none' && drawn !== 'normal') out.push(`${key}${pseudo} ${window.__look(node, pseudo)}`);
            }
          }
          return out;
        }
        """;

    // Runs in the page. A control of one of the four marks no toolbar shows, made of a copy of the editor and pressed.
    private const string Mark = """
        async ([selector, name]) => {
          const frame = () => new Promise(resolve => requestAnimationFrame(resolve));
          const first = document.querySelector(selector), copy = first.cloneNode(true);
          const button = copy.querySelector('[data-editor=bold]');
          button.setAttribute('data-editor', name);
          for (const state of ['aria-pressed', 'data-match']) button.removeAttribute(state);
          first.parentElement.appendChild(copy);
          window.__engine?.mount(copy);
          for (let i = 0; i < 200 && !copy.editor; i++) await frame();
          copy.editor.commands.setContent('<p>Hello world</p>', true);
          copy.editor.commands.focus();
          copy.editor.commands.selectAll();
          // A control is wired once the editor has said its state on it: until then a press is nobody's.
          for (let i = 0; i < 200 && !button.hasAttribute('aria-pressed'); i++) await frame();
          button.click();
          for (let i = 0; i < 200 && copy.value === '<p>Hello world</p>'; i++) await frame();
          await frame();
          await frame();
          const said = `${copy.value} pressed=${button.getAttribute('aria-pressed')} match=${button.hasAttribute('data-match')}`;
          copy.remove();
          return said;
        }
        """;

    private static readonly string[] Inherited = ["color", "fontFamily", "fontSize", "fontWeight", "lineHeight", "letterSpacing"];

    // Where each page keeps the same thing: Flux's custom elements, and the native ones the kit writes.
    private static readonly At Flux = new("ui-editor", "ui-options", "ui-option", "value", "ui-selected > div", "[data-flux-editor-link]", ["disabled", ""],
        "[data-editor=link] > ui-tooltip > [role=tooltip]");

    private static readonly At Rask = new("[data-ui-editor]", "[role=listbox]", "[role=option]", "data-value", "button[role=combobox] [data-value]", "[data-ui-editor-link]",
        ["aria-disabled", "true"], null);

    private sealed record At(string Editor, string List, string Option, string Value, string Shown, string Link, string[] Disabled, string? LinkTip)
    {
        public JsonObject Json() => new()
        {
            ["editor"] = Editor,
            ["list"] = List,
            ["option"] = Option,
            ["value"] = Value,
            ["shown"] = Shown,
            ["link"] = Link,
            ["disabled"] = new JsonArray(Disabled.Select(d => (JsonNode?)d).ToArray()),
            ["linkTip"] = LinkTip,
        };
    }

    private sealed record Transcript(List<string> Lines, JsonObject Inherited, List<long?> Stamps, List<(long At, string Event)> Trace);

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var arg = new Args(args);
        var raskPage = Repo.Parity("rask", "editor.html");
        if (!File.Exists(raskPage))
        {
            Console.Error.WriteLine($"flux parity: {raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages");
            return 1;
        }

        var engine = await File.ReadAllTextAsync(Repo.Path("src", "Rask.Ui", "Resources", "ui-editor.js"));
        var view = View.Of(arg);
        var browser = await Browsers.Chromium();
        var failures = 0;
        foreach (var scheme in new[] { "light", "dark" })
        {
            var theirs = await new Session(browser, view, scheme, Flux, engine).Take("https://fluxui.dev/components/editor", null);
            var mine = await new Session(browser, view, scheme, Rask, engine).Take(Repo.FileUrl(raskPage), theirs.Inherited);
            var length = Math.Max(theirs.Lines.Count, mine.Lines.Count);
            var differing = 0;
            for (var i = 0; i < length; i++)
            {
                var a = Nth(theirs.Lines, i);
                var b = Nth(mine.Lines, i);
                if (a == b || Pressables.CursorDepartedLine(a, b))
                {
                    continue;
                }

                if (differing++ < (arg.Flag("all") ? int.MaxValue : 12))
                {
                    Console.WriteLine($"FAIL {scheme} step {i}\n   Flux: {a ?? "undefined"}\n   Rask: {b ?? "undefined"}");
                    Console.WriteLine($"   Flux, on the way:\n{LeadingTo(theirs, i)}\n   Rask, on the way:\n{LeadingTo(mine, i)}");
                }
            }

            failures += differing;
            Console.WriteLine($"{(differing > 0 ? "FAIL" : "ok  ")} {scheme}: {length} observations{(differing > 0 ? $", {differing} differ" : "")}");
        }

        await browser.CloseAsync();
        Console.WriteLine(failures > 0 ? $"\nflux parity: the editor in use differs in {failures} observation(s)." : "\nflux parity: the editor in use matches Flux.");
        return failures > 0 ? 1 : 0;
    }

    private static string? Nth(List<string> lines, int i) => i < lines.Count ? lines[i] : null;

    // What a page did on its way to step i, from the step before: focus moving, keys, popovers opening and shutting,
    // each with the milliseconds since that step. A difference in focus or in a panel is read from this, not guessed.
    private static string LeadingTo(Transcript transcript, int i)
    {
        var until = i < transcript.Stamps.Count ? transcript.Stamps[i] : null;
        var from = transcript.Stamps.Take(i).LastOrDefault(stamp => stamp is not null) ?? 0;
        var events = until is null ? [] : transcript.Trace.Where(entry => entry.At >= from && entry.At <= until).Select(entry => $"     +{entry.At - from} ms {entry.Event}").ToList();
        return events.Count > 0 ? string.Join('\n', events) : "     (nothing)";
    }

    private sealed class Session(IBrowser browser, View view, string scheme, At at, string engine)
    {
        private readonly bool phone = view.Phone;
        private readonly List<string> lines = [];
        private readonly List<long?> stamps = [];
        private IPage page = null!;
        private ILocator editor = null!;

        private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private const string LinkButton = "[data-editor=link] button:not([data-editor])";
        private const string LinkPanel = "[data-editor=link] > [popover]";

        public async Task<Transcript> Take(string url, JsonObject? inherited)
        {
            var options = view.Context();
            options.ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light;
            var context = await browser.NewContextAsync(options);
            await context.AddInitScriptAsync($"window.__pressable = {Js.Quote(Pressables.Pressable)}; window.__look = {Look}; window.__states = {States}");
            await context.AddInitScriptAsync(Trace);
            page = await context.NewPageAsync();
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
            await page.Run("() => document.fonts.ready.then(() => undefined)");
            if (inherited is not null)
            {
                await page.Run(Mount, new JsonObject { ["engine"] = engine, ["inherited"] = inherited.DeepClone() });
            }
            else
            {
                inherited = (JsonObject)(await page.Run("keys => Object.fromEntries(keys.map(key => [key, getComputedStyle(document.querySelector('[data-preview-wrapper]'))[key]]))", Inherited))!;
            }

            editor = page.Locator(at.Editor).First;
            await editor.ScrollIntoViewIfNeededAsync();
            await Walk();

            var trace = (await page.Run("() => window.__trace") as JsonArray ?? [])
                .Select(entry => ((long)Js.Number(entry![0]), Js.Str(entry[1]) ?? "")).ToList();
            await context.CloseAsync();
            return new Transcript(lines, inherited, stamps, trace);
        }

        // A click, or a tap on a phone.
        private Task Hit(ILocator target) => phone ? target.TapAsync() : target.ClickAsync();

        // A gray's hue means nothing at zero chroma, and prints as `none` once a minifier has been at the sheet.
        // When each step was written down (Date.now, the clock the page's trace keeps too).
        private void Note(string what, string? value)
        {
            lines.Add($"{what}: {value ?? "undefined"}".Replace(" none)", " 0)", StringComparison.Ordinal));
            stamps.Add(Now);
        }

        private void Line(string line)
        {
            lines.Add(line.Replace(" none)", " 0)", StringComparison.Ordinal));
            stamps.Add(null);
        }

        private ILocator Content => editor.Locator("[data-slot=content]");

        private async Task<string?> Value() => Js.Str(await editor.Run("el => el.value"));

        // Both editors settle a frame or two after a change or a click: an observation waits for that.
        private Task Settle() => page.WaitForTimeoutAsync(100);

        private async Task Set(string html)
        {
            await editor.Run("(el, html) => el.editor.commands.setContent(html, true)", html);
            await Settle();
        }

        private async Task<string?> State(string selector) => Js.Str(await editor.Run("(el, selector) => window.__states(el.querySelector(selector))", selector));

        private async Task<string?> Focus() => Js.Str(await page.Run("() => { const a = document.activeElement; return a.dataset.slot ?? a.dataset.editor ?? a.closest('[data-editor]')?.dataset.editor ?? a.tagName; }"));

        private async Task<string?> Events() => Js.Str(await page.Run("() => window.__events.splice(0).join(',')"));

        // A popover's box against the button that opened it, and whether it is in the top layer.
        private async Task<string?> Placed(string panel, string trigger) => Js.Str(await editor.Run("""
            (el, [panel, trigger]) => {
              const p = el.querySelector(panel), a = p.getBoundingClientRect(), b = el.querySelector(trigger).getBoundingClientRect();
              return `${getComputedStyle(p).display} top-layer=${p.matches(':popover-open')} dx=${Math.round(a.x - b.x)} dy=${Math.round(a.y - b.bottom)} ${Math.round(a.width)}x${Math.round(a.height)}`;
            }
            """, new[] { panel, trigger }));

        private async Task<string?> Display(string panel) => Js.Str(await editor.Run("(el, panel) => getComputedStyle(el.querySelector(panel)).display", panel));

        private async Task<string?> Options(string name) => Js.Str(await editor.Run("""
            (el, [name, at]) => [...el.querySelectorAll(`[data-editor=${name}] ${at.option}`)].map(o =>
              `${o.getAttribute(at.value)}${o.hasAttribute('data-active') ? '@' : ''}${o.hasAttribute('data-selected') ? '*' : ''}[${o.getAttribute('aria-selected')}] bg=${getComputedStyle(o).backgroundColor} svg=${getComputedStyle(o.querySelector('svg')).color}`).join(' | ')
            """, new JsonArray(name, at.Json())));

        private async Task<string?> Shown(string name) => Js.Str(await editor.Run("(el, [name, at]) => el.querySelector(`[data-editor=${name}] ${at.shown}`)?.dataset.value", new JsonArray(name, at.Json())));

        private static string Select(string name) => $"[data-editor={name}] button";

        // The link panel is open once its address field has focus. Flux hands it focus a task or more after the press
        // (20 ms here, 130 ms at a sixth of the CPU, past 300 ms on a loaded runner, where Escape then found focus still
        // on the button and the panel shut with it there: `focus=link`, #1260); the kit does it in the press. Waited on, on
        // both pages, as long as it takes — and ten seconds at most, then on as before. Looked at on a timer, not on the
        // page's frames: a headless page may paint none (Screen.Frames), and a frame-polled wait then waits it all out.
        // The link button pressed as a person's mouse presses it: on it first, its tooltip showing, then the press (which
        // shuts the tooltip). Moved and pressed at once, Flux's tooltip came up a task AFTER the press, beside the open
        // panel, and Escape then put focus in the document; with nothing showing it gives it back to the button. Which one
        // happened was a race: the document on a Mac and in a container, the button on the loaded runner (#1260). A tap
        // has no rest on the button, so a phone still taps at once. (The kit's tooltip runtime is not on its page.)
        private async Task PressLink()
        {
            var button = editor.Locator(LinkButton);
            if (!phone && at.LinkTip is not null)
            {
                await button.HoverAsync();
                await Waited("([tip]) => document.querySelector(tip)?.matches(':popover-open')", new[] { $"{at.Editor} {at.LinkTip}" });
            }

            await Hit(button);
        }

        private async Task LinkOpened()
        {
            await page.WaitForTimeoutAsync(300);
            await Waited("() => document.activeElement?.dataset?.editor === 'link:url'", null);
        }

        private async Task Waited(string expression, object? arg)
        {
            try
            {
                await page.WaitForFunctionAsync(expression, arg, new PageWaitForFunctionOptions { PollingInterval = 50, Timeout = 10000 });
            }
            catch (TimeoutException)
            {
                // ten seconds, then on as before
            }
        }

        private async Task Press(string key) => await page.Keyboard.PressAsync(key);

        private Task Wait(float ms) => page.WaitForTimeoutAsync(ms);

        private async Task Walk()
        {
            await editor.Run("""
                el => {
                  window.__events = [];
                  for (const type of ['input', 'change']) el.addEventListener(type, event => window.__events.push(`${type}${event.bubbles ? '^' : ''}`));
                }
                """);

            await Document();
            await Toggles();
            await Typing();
            await Lists();
            await Link();
            await Toolbar();
            await Disabled();
            await Marks();
        }

        // The document: every kind of node, as it computes.
        private async Task Document()
        {
            await Set(ParityEditor.Document);
            Note("events of a programmatic change", await Events());
            foreach (var line in await Content.Run(Nodes) as JsonArray ?? [])
            {
                Line(Js.Str(line) ?? "");
            }

            Note("area", Js.Str(await Content.Run("c => window.__look(c)")));
            Note("value", await Value());
            await Set("");
            Note("empty value", await Value() is { } empty ? Js.Quote(empty) : "undefined");
            Note("empty paragraph", Js.Str(await Content.Run("c => { const p = c.querySelector('p'); return `${p.className} placeholder=${p.getAttribute('data-placeholder')} ${window.__look(p, '::before')}`; }")));
            Note("placeholder of the second editor", Js.Str(await page.Locator(at.Editor).Nth(1).Run("el => { const p = el.querySelector('[data-slot=content] p'); return `${p.getAttribute('data-placeholder')} ${window.__look(p, '::before')}`; }")));
        }

        // Toggles: by button, by shortcut, and what the button says.
        private async Task Toggles()
        {
            await Set("<p>Hello world</p>");
            await Hit(Content);
            await Settle();
            await Events();
            Note("focused area", Js.Str(await editor.Run("el => window.__look(el.querySelector('[data-slot=content]')) + ' root ' + getComputedStyle(el).boxShadow.slice(-40)")));
            await Press("ControlOrMeta+a");
            await Settle();
            Note("bold at rest", await State("[data-editor=bold]"));
            await Hit(editor.Locator("[data-editor=bold]"));
            await Settle();
            Note("bold pressed", $"{await State("[data-editor=bold]")} value={await Value()} events={await Events()} focus={await Focus()}");
            await Press("ControlOrMeta+b");
            Note("bold by shortcut", $"{await State("[data-editor=bold]")} value={await Value()}");
            foreach (var (name, keys) in new[] { ("italic", "ControlOrMeta+i"), ("strike", "ControlOrMeta+Shift+s"), ("bullet", "ControlOrMeta+Shift+8"), ("ordered", "ControlOrMeta+Shift+7"), ("blockquote", "ControlOrMeta+Shift+b") })
            {
                await Set("<p>Hello world</p><p>second</p>");
                await Hit(Content.Locator("p").First);
                await Settle();
                for (var press = 1; press <= 2; press++)
                {
                    await Hit(editor.Locator($"[data-editor={name}]"));
                    await Wait(120);
                    Note($"{name} click {press}", $"{await State($"[data-editor={name}]")} value={await Value()} focus={await Focus()}");
                }

                await Press(keys);
                Note($"{name} by shortcut", $"{await State($"[data-editor={name}]")} value={await Value()}");
            }

            foreach (var keys in new[] { "ControlOrMeta+u", "ControlOrMeta+e", "ControlOrMeta+Shift+h", "ControlOrMeta+,", "ControlOrMeta+.", "ControlOrMeta+Alt+2", "ControlOrMeta+Alt+0", "ControlOrMeta+Shift+e", "ControlOrMeta+Shift+r" })
            {
                await Set("<p>Hello world</p>");
                await Hit(Content);
                await Settle();
                await Press("ControlOrMeta+a");
                await Settle();
                await Wait(60);
                await Press(keys);
                await Wait(60);
                Note($"shortcut {keys}", await Value());
            }
        }

        // Typing, undo, redo, and the Markdown rules.
        private async Task Typing()
        {
            await Set("<p>Hello world</p>");
            await Hit(Content);
            await Settle();
            await Press("ControlOrMeta+a");
            await Settle();
            await Events();
            // Both editors' history joins changes less than 500 ms apart into one undo, and the document set above was
            // ~450 ms ago: undo took the typing alone on one run and the typing with the set document on the next, on
            // either page (`<p><sup>Hello world</sup></p>`, the shortcut before it). Typed past that, undo is the typing.
            await Wait(600);
            await page.Keyboard.TypeAsync("ab");
            Note("two characters typed", $"{await Value()} events={await Events()}");
            await Press("ControlOrMeta+z");
            Note("undo", await Value());
            await Press("ControlOrMeta+Shift+z");
            Note("redo", await Value());
            await Press("ControlOrMeta+a");
            await Settle();
            await Press("Backspace");
            foreach (var text in new[] { "# Title\n", "## Second\n", "### Third\n", "- item\n\n", "1. one\n\n", "> quote\n\n", "**bold** *italic* ~~struck~~ `code` \n", "---\n", "```js\nlet a = 1;" })
            {
                await page.Keyboard.TypeAsync(text, new KeyboardTypeOptions { Delay = 5 });
            }

            Note("markdown input rules", await Value());
            await Press("ControlOrMeta+Shift+Enter");

            // The toolbar as it lays out: one row, and on a narrow screen a row that scrolls.
            Note("toolbar", Js.Str(await editor.Run("""
                el => {
                  const bar = el.querySelector('[role=toolbar]'), at = bar.getBoundingClientRect();
                  const scroller = [bar, ...bar.querySelectorAll('*')].find(n => n.scrollWidth > n.clientWidth && getComputedStyle(n).overflowX !== 'visible');
                  const boxes = [...bar.querySelectorAll('button')].filter(b => b.offsetParent).map(b => { const r = b.getBoundingClientRect(); return `${Math.round(r.x - at.x)},${Math.round(r.y - at.y)}`; });
                  return `${Math.round(at.width)}x${Math.round(at.height)} scrolls=${scroller ? `${scroller.scrollWidth}/${scroller.clientWidth}` : 'no'} ${boxes.join(' ')}`;
                }
                """)));
        }

        // The heading list, then the align list.
        private async Task Lists()
        {
            var heading = Select("heading");
            var headingList = $"[data-editor=heading] {at.List}";
            await Set("<p>Hello world</p>");
            await Hit(Content);
            await Settle();
            Note("heading closed", await State(heading));
            await Hit(editor.Locator(heading));
            await Wait(300);
            Note("heading open", $"{await State(heading)} list {await Placed(headingList, heading)} focus={await Focus()}");
            Note("heading options", await Options("heading"));
            await Press("ArrowDown");
            Note("heading ArrowDown", await Options("heading"));
            await Press("ArrowUp");
            await Press("ArrowUp");
            Note("heading ArrowUp at the top", await Options("heading"));
            await Press("ArrowDown");
            await Press("Enter");
            await Wait(200);
            Note("heading Enter", $"{await Value()} {await State(heading)} list {await Display(headingList)} shown={await Shown("heading") ?? "undefined"} focus={await Focus()}");
            await Hit(editor.Locator(heading));
            await Wait(300);
            var third = (await editor.Locator($"[data-editor=heading] {at.Option}").Nth(3).Run("o => { const box = o.getBoundingClientRect(); return [box.x + 20, box.y + 10]; }") as JsonArray)!
                .Select(Js.Number).ToArray();
            if (!phone)
            {
                await page.Mouse.MoveAsync((float)third[0], (float)third[1], new MouseMoveOptions { Steps = 4 });
                Note("heading pointer over an option", await Options("heading"));
            }

            await Screen.Press(page, third[0], third[1], phone);
            await Wait(200);
            Note("heading option clicked", $"{await Value()} shown={await Shown("heading") ?? "undefined"} focus={await Focus()}");
            await Hit(editor.Locator(heading));
            await Wait(300);
            await Press("Escape");
            await Wait(150);
            Note("heading Escape", $"{await State(heading)} focus={await Focus()}");
            await Hit(editor.Locator(heading));
            await Wait(300);
            await Screen.Press(page, 5, 5, phone);
            await Wait(150);
            Note("heading press outside", $"{await State(heading)} list {await Display(headingList)} focus={await Focus()}");
            await Set("<h2>Head</h2><p>para</p>");
            await Hit(Content.Locator("h2"));
            await Settle();
            Note("caret in a heading", $"shown={await Shown("heading") ?? "undefined"} {await Options("heading")}");

            var align = Select("align");
            await Set("<p>Hello world</p>");
            await Hit(Content);
            await Settle();
            await Hit(editor.Locator(align));
            await Wait(300);
            Note("align open", $"{await State(align)} list {await Placed($"[data-editor=align] {at.List}", align)}");
            await Press("ArrowDown");
            await Press("Enter");
            await Wait(200);
            Note("align center", $"{await Value()} shown={await Shown("align") ?? "undefined"}");
            await Press("ControlOrMeta+Shift+l");
            Note("align left by shortcut", $"{await Value()} shown={await Shown("align") ?? "undefined"}");
        }

        // The link panel.
        private async Task Link()
        {
            await Set("<p>Hello world</p>");
            await Hit(Content);
            await Settle();
            await Press("ControlOrMeta+a");
            await Settle();
            await PressLink();
            await LinkOpened();
            Note("link open", $"{await State(LinkButton)} panel {await Placed(LinkPanel, LinkButton)} focus={await Focus()}");
            Note("link panel look", Js.Str(await editor.Run("""
                (el, at) => {
                  const q = selector => getComputedStyle(el.querySelector(selector));
                  const input = q('[data-editor="link:url"]'), insert = q('[data-editor="link:insert"]'), row = q(at.link), panel = q('[data-editor=link] > [popover]');
                  return `panel bg=${panel.backgroundColor} border=${panel.borderTopColor} input ${input.color} ${input.fontSize}/${input.lineHeight} h=${input.height} outline=${input.outlineStyle} insert ${insert.color} ${insert.width}x${insert.height} row h=${row.height}`;
                }
                """, at.Json())));
            await page.Keyboard.TypeAsync("example.com");
            await Press("Enter");
            await Wait(200);
            if (!phone)
            {
                await page.Mouse.MoveAsync(640, 4);
            }

            await Settle();
            Note("link Enter", $"{await Value()} {await State(LinkButton)} panel {await Display(LinkPanel)} focus={await Focus()}");
            await PressLink();
            await LinkOpened();
            Note("link reopened", await editor.Locator("[data-editor=\"link:url\"]").InputValueAsync());
            await Press("Escape");
            await Wait(150);
            Note("link Escape, opened by its button", $"{await State(LinkButton)} panel {await Display(LinkPanel)} focus={await Focus()}");
            // By the keyboard: Enter on the focused button, the pointer elsewhere.
            if (!phone)
            {
                await page.Mouse.MoveAsync(640, 4);
            }

            await editor.Locator(LinkButton).FocusAsync();
            await Press("Enter");
            await LinkOpened();
            await Press("Escape");
            await Wait(150);
            Note("link Escape, opened by Enter on its button", $"{await State(LinkButton)} panel {await Display(LinkPanel)} focus={await Focus()}");
            await PressLink();
            await LinkOpened();
            await Hit(editor.Locator("[data-editor=\"link:unlink\"]"));
            await Wait(200);
            Note("unlink", $"{await Value()} {await State(LinkButton)} focus={await Focus()}");
            await Hit(Content);
            await Settle();
            await Press("ControlOrMeta+a");
            await Settle();
            await Press("ControlOrMeta+k");
            await LinkOpened();
            Note("link by shortcut", $"panel {await Placed(LinkPanel, LinkButton)} focus={await Focus()}");
            await Press("Escape");
            await Wait(150);
            Note("link Escape, opened by the shortcut", $"{await State(LinkButton)} panel {await Display(LinkPanel)} focus={await Focus()}");
            await PressLink();
            await LinkOpened();
            await editor.Locator("[data-editor=\"link:url\"]").FillAsync("https://x.y");
            await Hit(editor.Locator("[data-editor=\"link:insert\"]"));
            await Wait(200);
            Note("link insert button", $"{await Value()} focus={await Focus()}");
        }

        // The toolbar's keys: one tab stop, the arrows walk it.
        private async Task Toolbar()
        {
            async Task<string?> Stops() => Js.Str(await editor.Run("el => [...el.querySelectorAll('[role=toolbar] button')].filter(b => b.offsetParent).map(b => `${b.dataset.editor ?? b.closest('[data-editor]')?.dataset.editor}:${b.tabIndex}${b === document.activeElement ? '*' : ''}`).join(' ')"));
            await editor.Locator(Select("heading")).FocusAsync();
            Note("toolbar focused", await Stops());
            foreach (var key in new[] { "ArrowRight", "ArrowRight", "ArrowRight", "ArrowLeft", "Home", "End", "ArrowDown" })
            {
                await Press(key);
                await Wait(120);
                Note($"toolbar {key}", await Stops());
            }

            await Press("Escape");
            await Press("Tab");
            Note("toolbar Tab", await Focus());
            await Press("Shift+Tab");
            Note("area Shift+Tab", await Focus());

            // One at a time, as a person presses them: Flux moves focus a frame after the key.
            async Task Walked(string key, int presses)
            {
                for (var press = 0; press < presses; press++)
                {
                    await Press(key);
                    await Wait(120);
                }
            }

            await Wait(200);
            await Walked("ArrowLeft", 4);
            Note("toolbar ArrowLeft past the start", await Stops());
            await Walked("ArrowRight", 12);
            Note("toolbar ArrowRight past the end", await Stops());
        }

        private async Task Disabled()
        {
            await editor.Run("(el, [name, value]) => el.setAttribute(name, value)", at.Disabled);
            await Wait(200);
            Note("disabled", Js.Str(await editor.Run("""
                el => {
                  const area = el.querySelector('[data-slot=content]'), bold = el.querySelector('[data-editor=bold]');
                  return `shadow=${getComputedStyle(el).boxShadow.slice(-36)} area ${getComputedStyle(area).color} editable=${area.getAttribute('contenteditable')} bold disabled=${bold.disabled} opacity=${getComputedStyle(bold).opacity} select disabled=${el.querySelector('[data-editor=heading] button').disabled}`;
                }
                """)));
            await editor.Run("(el, [name]) => el.removeAttribute(name)", at.Disabled);
            await Wait(200);
            Note("enabled again", Js.Str(await editor.Run("el => `editable=${el.querySelector('[data-slot=content]').getAttribute('contenteditable')} bold disabled=${el.querySelector('[data-editor=bold]').disabled}`")));
        }

        // The four marks no example's toolbar shows. Flux's element wires a control by its name, so a control of that name
        // is made — the bold button of a copy of the editor, renamed — and pressed over a selection: what `code` does (the
        // inline mark, not the block) is read here and nowhere else. Waits are on state.
        private async Task Marks()
        {
            foreach (var name in new[] { "code", "subscript", "superscript", "highlight" })
            {
                Note($"a control named {name}", Js.Str(await page.Run(Mark, new[] { at.Editor, name })));
            }
        }
    }
}
