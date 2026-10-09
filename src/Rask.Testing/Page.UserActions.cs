using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Rask.Core.Routing;

namespace Rask.Testing;

// The page, driven the way a person drives it: type into the field labelled "Name", click the button that says
// "Save", see "Product saved". Every element is found by what a user sees — a label, a placeholder, a button's
// text — never by a selector, so a test reads as the steps it proves and survives a markup change that a user
// would not notice. A lookup that finds nothing, or more than one thing, fails naming what it did find.
public partial class Page
{
    /// <summary>How long <see cref="Shows(string)" /> and <see cref="DoesNotShow" /> wait for async work to land. Default 5 s.</summary>
    public TimeSpan Patience { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Types <paramref name="text" /> into a field: <c>await page.Type("Tea").Into("Name");</c></summary>
    public Typing Type(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new Typing(this, text);
    }

    /// <summary>Picks an option of a select by its visible text: <c>await page.Pick("Green").From("Colour");</c></summary>
    public Picking Pick(string option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return new Picking(this, option);
    }

    /// <summary>Ticks the checkbox labelled <paramref name="label" />: <c>await page.Check("In stock");</c></summary>
    public Task Check(string label) => SetChecked(label, true);

    /// <summary>Clears the checkbox labelled <paramref name="label" />.</summary>
    public Task Uncheck(string label) => SetChecked(label, false);

    /// <summary>
    ///     Clicks the button or link whose text (or aria-label) is <paramref name="text" />:
    ///     <c>await page.Click("Save");</c>. A submit button submits its form. Scope an ambiguous one with
    ///     <c>.In("Tea")</c>.
    /// </summary>
    public Clicking Click(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new Clicking(this, text, region: null);
    }

    /// <summary>
    ///     Checks that the page shows <paramref name="text" />, re-rendering for up to <see cref="Patience" /> while
    ///     async work lands: <c>page.Shows("Product saved");</c>. Narrow it with <c>.In("Products")</c>.
    /// </summary>
    /// <exception cref="PageException">The text never appeared; the message carries what the page does show.</exception>
    public Showing Shows(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Await(root => VisibleText(root).Contains(text, StringComparison.Ordinal), () =>
            $"Expected the page to show \"{text}\" within {Seconds(Patience)}. It shows:\n  {VisibleText(Root)}");
        return new Showing(this, text);
    }

    /// <summary>
    ///     Checks that the page's markup satisfies <paramref name="condition" />, re-rendering for up to
    ///     <see cref="Patience" /> while async work lands: <c>page.Shows(html =&gt; html.Contains("data-state=\"done\""))</c>.
    /// </summary>
    /// <exception cref="PageException">The condition never held; the message carries the last markup.</exception>
    public void Shows(Func<string, bool> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        Await(_ => condition(Html), () =>
            $"Expected the page's markup to satisfy the condition within {Seconds(Patience)}. It is:\n  {Html}");
    }

    /// <summary>Checks that the page does not show <paramref name="text" />, waiting for it to go if it is still there.</summary>
    public void DoesNotShow(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Await(root => !VisibleText(root).Contains(text, StringComparison.Ordinal), () =>
            $"Expected the page not to show \"{text}\", but after {Seconds(Patience)} it still does:\n  {VisibleText(Root)}");
    }

    /// <summary>Checks that the app navigated to <paramref name="path" /> — for a page opened with <c>Page.Visit</c>.</summary>
    public void IsAt(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var state = _services.GetService(typeof(RouteState)) as RouteState
            ?? throw new PageException("IsAt needs a routed page. Open it with Page.Visit(\"/…\") rather than Page.Render.");

        var at = state.Path + (state.Query.Count == 0 ? "" : "?" + string.Join('&', state.Query.Select(q => $"{q.Key}={q.Value}")));
        if (!string.Equals(Trim(at), Trim(path), StringComparison.Ordinal) && !string.Equals(Trim(state.Path), Trim(path), StringComparison.Ordinal))
        {
            throw new PageException($"Expected to be at \"{path}\", but the page is at \"{at}\".");
        }

        static string Trim(string p) => p.Length > 1 ? p.TrimEnd('/') : p;
    }

    // ---- the verbs' second halves ------------------------------------------------------------------------------

    /// <summary>A <see cref="Type" /> still missing its field.</summary>
    public readonly struct Typing(Page page, string text)
    {
        /// <summary>Types into the field labelled <paramref name="field" /> (label, then placeholder, then aria-label).</summary>
        public Task Into(string field) => page.Enter(page.Field(field), text, field);
    }

    /// <summary>A <see cref="Pick" /> still missing its select.</summary>
    public readonly struct Picking(Page page, string option)
    {
        /// <summary>Picks from the select labelled <paramref name="field" />.</summary>
        public Task From(string field) => page.Choose(field, option);
    }

    /// <summary>A <see cref="Click" /> still being worded; nothing is clicked until it is awaited.</summary>
    public readonly struct Clicking
    {
        private readonly Page _page;
        private readonly string _text;
        private readonly string? _region;

        internal Clicking(Page page, string text, string? region)
        {
            _page = page;
            _text = text;
            _region = region;
        }

        /// <summary>Clicks the one inside the region named <paramref name="region" /> — a row, a section, a form.</summary>
        public Clicking In(string region) => new(_page, _text, region ?? throw new ArgumentNullException(nameof(region)));

        /// <summary>Clicks it.</summary>
        public TaskAwaiter GetAwaiter() => _page.Press(_text, _region).GetAwaiter();
    }

    /// <summary>What <see cref="Shows(string)" /> saw, to narrow to a region.</summary>
    public readonly struct Showing(Page page, string text)
    {
        /// <summary>Checks that the text is inside the region named <paramref name="region" />.</summary>
        public void In(string region)
        {
            var (on, shown) = (page, text);
            on.Await(root => VisibleText(Region(root, region)).Contains(shown, StringComparison.Ordinal), () =>
                $"Expected \"{region}\" to show \"{shown}\". It shows:\n  {VisibleText(Region(on.Root, region))}");
        }
    }

    // ---- finding things the way a user does -------------------------------------------------------------------

    private static readonly string[] FieldTags = ["input", "textarea", "select"];

    internal HtmlNode Field(string label)
    {
        var root = Root;
        var fields = root.DescendantsAndSelf().Where(IsField).ToList();

        var byLabel = root.DescendantsAndSelf()
            .Where(n => string.Equals(n.Tag, "label", StringComparison.Ordinal) && Same(n.TextContent, label))
            .Select(l => l.Attribute("for") is { } id
                ? fields.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal))
                : l.DescendantsAndSelf().FirstOrDefault(IsField))
            .OfType<HtmlNode>()
            .ToList();
        if (byLabel.Count == 0)
        {
            byLabel = fields.Where(f => Same(f.Attribute("placeholder"), label) || Same(f.Attribute("aria-label"), label)).ToList();
        }

        return byLabel.Count switch
        {
            1 => byLabel[0],
            0 => throw new PageException(
                $"No field is labelled \"{label}\". The fields on the page are: {Describe(fields.Select(FieldName))}."),
            _ => throw new PageException($"{byLabel.Count} fields are labelled \"{label}\" — give them different labels."),
        };
    }

    private static HtmlNode Region(HtmlNode root, string name)
    {
        // The smallest container that is named by the region — its aria-label, a heading, legend, caption or label
        // inside it — or, for a row or a list item, that simply contains the text.
        var regions = root.DescendantsAndSelf()
            .Where(n => n.Tag is "section" or "article" or "form" or "fieldset" or "dialog" or "table" or "ul" or "ol" or "nav"
                            or "aside" or "main" or "header" or "footer" or "tr" or "li" or "div"
                        && (Same(n.Attribute("aria-label"), name)
                            || n.Children.Any(c => c.Tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "legend" or "caption"
                                                   && Same(c.TextContent, name))
                            || (n.Tag is "tr" or "li" && Normalize(n.TextContent).Contains(name, StringComparison.Ordinal))))
            .ToList();

        // Deepest first: a row inside a table named the same wins over the table.
        var region = regions.OrderByDescending(Depth).FirstOrDefault();
        return region ?? throw new PageException(
            $"No region is named \"{name}\" — no section, form, row or list item with that label, heading or text.");
    }

    private HtmlNode Target(string text, string? region)
    {
        var scope = region is null ? Root : Region(Root, region);
        var candidates = scope.DescendantsAndSelf().Where(IsClickable).ToList();
        var matches = candidates
            .Where(n => Same(n.TextContent, text) || Same(n.Attribute("aria-label"), text) || Same(n.Attribute("value"), text)
                        || Same(n.Attribute("title"), text))
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new PageException(
                $"Nothing clickable says \"{text}\"{(region is null ? "" : $" in \"{region}\"")}. "
                + $"What can be clicked: {Describe(candidates.Select(ClickableName))}."),
            _ => throw new PageException(
                $"Clicking \"{text}\" found {matches.Count}: {Describe(matches.Select(n => n.Path()))}. "
                + $"Say which with .In(\"…\") — e.g. page.Click(\"{text}\").In(\"{Nearest(matches[0])}\")."),
        };
    }

    // ---- doing things -----------------------------------------------------------------------------------------

    // A bound control says nothing until the next action (`data-rask-bind-on="action"`), in a browser and here:
    // what was typed, picked or ticked is kept, and goes ahead of the next click — or of a control that does
    // speak at once. So a test that reads the model straight after typing fails as the page would.
    private readonly List<(string Label, string Payload)> _waiting = [];

    private static bool WaitsForAction(HtmlNode field) =>
        string.Equals(field.Attribute("data-rask-bind-on"), "action", StringComparison.Ordinal);

    private void Keep(string label, string payload)
    {
        _waiting.RemoveAll(kept => string.Equals(kept.Label, label, StringComparison.Ordinal));
        _waiting.Add((label, payload));
    }

    // Sends what waits, in the order it was last touched. Each one re-renders, so each field is found afresh.
    internal async Task SendWhatWaits()
    {
        while (_waiting.Count > 0)
        {
            var (label, payload) = _waiting[0];
            _waiting.RemoveAt(0);
            await Raise(Field(label), "change", payload).ConfigureAwait(false);
        }
    }

    private async Task Enter(HtmlNode field, string text, string label)
    {
        if (WaitsForAction(field))
        {
            Keep(label, Payload(text));
            return;
        }

        await SendWhatWaits().ConfigureAwait(false);
        field = Field(label);
        var input = await Raise(field, "input", Payload(text)).ConfigureAwait(false);
        var change = await Raise(Field(label), "change", Payload(text)).ConfigureAwait(false);
        if (!input && !change)
        {
            throw new PageException(
                $"The field \"{label}\" has no input or change handler, so typing into it changes nothing. Bind it — "
                + $"Input.Bind(() => model.{label.Replace(" ", "")}) — or give it .OnInput(…).");
        }
    }

    private async Task Choose(string label, string option)
    {
        var select = Field(label);
        var options = select.DescendantsAndSelf().Where(n => string.Equals(n.Tag, "option", StringComparison.Ordinal)).ToList();
        var chosen = options.FirstOrDefault(o => Same(o.TextContent, option))
            ?? throw new PageException(
                $"\"{label}\" has no option \"{option}\". Its options are: {Describe(options.Select(o => Normalize(o.TextContent)))}.");

        var value = chosen.Attribute("value") ?? Normalize(chosen.TextContent);
        if (WaitsForAction(select))
        {
            Keep(label, Payload(value));
            return;
        }

        await SendWhatWaits().ConfigureAwait(false);
        select = Field(label);
        var change = await Raise(select, "change", Payload(value)).ConfigureAwait(false);
        var input = await Raise(Field(label), "input", Payload(value)).ConfigureAwait(false);
        if (!change && !input)
        {
            throw new PageException($"The select \"{label}\" has no change handler, so picking changes nothing.");
        }
    }

    private async Task SetChecked(string label, bool on)
    {
        ArgumentNullException.ThrowIfNull(label);
        var box = Field(label);
        if (!string.Equals(box.Attribute("type"), "checkbox", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(box.Attribute("type"), "radio", StringComparison.OrdinalIgnoreCase))
        {
            throw new PageException($"\"{label}\" is a {FieldName(box)}, not a checkbox.");
        }

        if (WaitsForAction(box))
        {
            Keep(label, Payload(on ? "true" : "false"));
            return;
        }

        await SendWhatWaits().ConfigureAwait(false);
        if (!await Raise(Field(label), "change", Payload(on ? "true" : "false")).ConfigureAwait(false))
        {
            throw new PageException($"The checkbox \"{label}\" has no change handler, so ticking it changes nothing.");
        }
    }

    private async Task Press(string text, string? region)
    {
        await SendWhatWaits().ConfigureAwait(false);
        var target = Target(text, region);
        if (await Raise(target, "click", null).ConfigureAwait(false))
        {
            return;
        }

        if (IsSubmit(target) && Ancestors(target).FirstOrDefault(n => string.Equals(n.Tag, "form", StringComparison.Ordinal)) is { } form
            && form.Attribute("data-rask-on-submit") is { } submit)
        {
            await Invoke(submit, FormPayload(form)).ConfigureAwait(false);
            return;
        }

        if (string.Equals(target.Tag, "a", StringComparison.Ordinal) && target.Attribute("href") is { } href
            && _services.GetService(typeof(RouteState)) is RouteState state)
        {
            var url = TestRoute.At(href);
            state.Path = url.Path;
            state.Query = url.Query;
            Render();
            return;
        }

        throw new PageException($"Nothing handles a click on \"{text}\" — it has no click handler, submits no form and links nowhere.");
    }

    // True when the element had a handler for the event, which then ran.
    private async Task<bool> Raise(HtmlNode node, string domEvent, string? payload)
    {
        if (node.Attribute("data-rask-on-" + domEvent) is not { } id)
        {
            return false;
        }

        await Invoke(id, payload).ConfigureAwait(false);
        return true;
    }

    // What a browser posts: every named control's current value; an unticked checkbox posts nothing.
    private static string FormPayload(HtmlNode form)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var control in form.DescendantsAndSelf().Where(IsField))
        {
            if (control.Attribute("name") is not { Length: > 0 } name)
            {
                continue;
            }

            var type = control.Attribute("type")?.ToLowerInvariant();
            if (type is "checkbox" or "radio")
            {
                if (control.Attributes.ContainsKey("checked"))
                {
                    fields[name] = control.Attribute("value") ?? "on";
                }

                continue;
            }

            fields[name] = control.Tag switch
            {
                "textarea" => control.TextContent,
                "select" => control.DescendantsAndSelf().FirstOrDefault(o => string.Equals(o.Tag, "option", StringComparison.Ordinal) && o.Attributes.ContainsKey("selected"))
                    is { } o ? o.Attribute("value") ?? Normalize(o.TextContent) : "",
                _ => control.Attribute("value") ?? "",
            };
        }

        return JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal) { ["form"] = fields });
    }

    // ---- waiting ----------------------------------------------------------------------------------------------

    private void Await(Func<HtmlNode, bool> holds, Func<string> failure)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            Render();
            if (holds(Root))
            {
                return;
            }

            if (Stopwatch.GetElapsedTime(started) >= Patience)
            {
                throw new PageException(failure());
            }

            // A synchronous poll on the test's own thread: the assertion API is synchronous by design, so there is
            // no caller to hand an awaitable to.
#pragma warning disable RS0030
            Thread.Sleep(PollInterval);
#pragma warning restore RS0030
        }
    }

    // ---- small helpers ----------------------------------------------------------------------------------------

    private static bool IsField(HtmlNode n) =>
        FieldTags.Contains(n.Tag, StringComparer.Ordinal) && !string.Equals(n.Attribute("type"), "hidden", StringComparison.OrdinalIgnoreCase);

    private static bool IsClickable(HtmlNode n) =>
        n.Tag is "button" or "a" or "summary"
        || n.Attribute("role") is "button" or "link" or "tab" or "menuitem"
        || (string.Equals(n.Tag, "input", StringComparison.Ordinal) && n.Attribute("type") is "submit" or "button" or "reset")
        || n.Attributes.ContainsKey("data-rask-on-click");

    private static bool IsSubmit(HtmlNode n) =>
        (string.Equals(n.Tag, "button", StringComparison.Ordinal) && n.Attribute("type") is null or "submit")
        || (string.Equals(n.Tag, "input", StringComparison.Ordinal) && n.Attribute("type") is "submit");

    private static IEnumerable<HtmlNode> Ancestors(HtmlNode n)
    {
        for (var p = n.Parent; p is not null; p = p.Parent)
        {
            yield return p;
        }
    }

    private static int Depth(HtmlNode n) => Ancestors(n).Count();

    private static string Nearest(HtmlNode n) =>
        Ancestors(n).FirstOrDefault(a => a.Tag is "tr" or "li" or "form" or "section") is { } r
            ? Normalize(r.TextContent).Split(' ').FirstOrDefault() ?? "…"
            : "…";

    private static bool Same(string? actual, string expected) =>
        actual is not null && string.Equals(Normalize(actual).TrimEnd('*', ':', ' '), expected, StringComparison.Ordinal);

    private static string VisibleText(HtmlNode root) =>
        Normalize(string.Concat(root.DescendantsAndSelf()
            .Where(n => n.Tag is not ("script" or "style" or "template") && !Ancestors(n).Any(a => a.Tag is "script" or "style" or "template"))
            .Select(n => n.Text + " ")));

    private static string FieldName(HtmlNode f) =>
        f.Attribute("aria-label") ?? f.Attribute("placeholder") ?? f.Attribute("name") ?? f.Id ?? $"<{f.Tag}>";

    private static string ClickableName(HtmlNode n) =>
        Normalize(n.TextContent) is { Length: > 0 } t ? $"\"{t}\"" : n.Attribute("aria-label") ?? $"<{n.Tag}>";

    private static string Payload(string value) => "{\"value\":" + JsonSerializer.Serialize(value) + "}";

    private static string Seconds(TimeSpan t) => $"{t.TotalSeconds:0.#}s";
}
