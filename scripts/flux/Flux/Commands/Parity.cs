using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     Holds a Rask.Ui component to Flux UI's live documentation, example by example.
/// </summary>
/// <remarks>
///     Two measurements of the same examples, taken the same way (<see cref="Measure" />): Flux's docs page, and the page
///     FluxParityPages wrote for the Rask component (artifacts/flux-parity/rask/&lt;slug&gt;.html), compared by
///     <see cref="ParityComparer" />.
///     <para>
///         What is NOT the component's is taken out of the comparison in the open, each by one rule:
///         the docs page's inherited text (ink, font, line height) is copied onto each Rask example first;
///         a component from ANOTHER page is a stand-in until it is rebuilt: marked and <c>data-parity-skip</c>
///         (held to its place and size), or unmarked (a printed note, nothing compared);
///         <c>data-parity-skip="self|width|height"</c> lets go of exactly that;
///         a Flux custom element pairs with the native element that behaves that way (<see cref="ParityComparer.Native" />).
///     </para>
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages               # writes the Rask pages
///     flux parity button                           # components/button
///     flux parity layouts/sidebar --refresh        # re-measure Flux
///     flux parity button --section variants --all  # one section, every difference
///     </code>
///     Exit code 1 on any difference. Screenshots of both sides land beside the measurements.
/// </remarks>
[FluxCommand("parity", "a component's examples against Flux's docs page")]
public sealed class Parity : IFluxCommand
{
    // The docs page hands each example its text: black or white by scheme, a 24px or 26px line by where the
    // example sits in the prose. A component inherits those, so each Rask example is given what its Flux
    // twin was given before anything is measured — the same surroundings, and only the component differs.
    // In a viewport of its own the docs page's frame is narrower than this page's, so there each example is also
    // given its twin's width and padding: the room the component is laid out in.
    private const string Inherit = """
        ({ examples, INHERITED, FRAME, framed }) => {
          const seen = {};
          for (const wrapper of document.querySelectorAll('[data-preview-wrapper]')) {
            const section = wrapper.dataset.section ?? '';
            const ordinal = seen[section] = (seen[section] ?? -1) + 1;
            const theirs = examples.find(example => example.section === section && example.ordinal === ordinal);
            for (const key of theirs ? INHERITED : []) wrapper.style[key] = theirs.nodes[0].style[key];
            if (theirs && framed) {
              for (const key of FRAME) wrapper.style[key] = theirs.nodes[0].style[key];
              wrapper.style.width = `${theirs.size[0]}px`;
            }
            // The docs page's line height is a RATIO, so a node that sets its own font size and no line height
            // (a 9px number in an icon) gets that ratio of it. Copied as pixels it would get the wrapper's 24px.
            const { fontSize, lineHeight } = theirs?.nodes[0].style ?? {};
            if (lineHeight?.endsWith('px')) wrapper.style.lineHeight = String(parseFloat(lineHeight) / parseFloat(fontSize));
          }
        }
        """;

    private static readonly string[] Inherited = ["color", "fontFamily", "fontSize", "fontWeight", "lineHeight", "letterSpacing"];
    private static readonly string[] Frame = ["boxSizing", "paddingTop", "paddingRight", "paddingBottom", "paddingLeft"];

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var arg = new Args(args);
        // The page is the first argument that is no option's value — by its place, not its text: in
        // `parity command-open --prepare command-open` the page and the preparation share a name.
        var asked = arg.All.Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal) && (i == 0 || arg.All[i - 1] is not ("--section" or "--view" or "--prepare")))
            .FirstOrDefault();
        // `--view 390x844`: both pages are measured in that viewport — a component that is another thing on a phone.
        var view = arg.Option("view")?.Split('x').Select(int.Parse).ToArray();
        // `--prepare <name>`: a state the component is only in once it is USED (IParityPreparation); `<slug>` is then a
        // page of its own (parity-command: `command-open`).
        var prepared = arg.Option("prepare") is { } name ? ParityPreparations.Find(name) : null;
        if (asked is null)
        {
            Console.Error.WriteLine("usage: flux parity <page> [--refresh] [--section <id>] [--view <w>x<h>] [--prepare <name>] [--all]");
            return 1;
        }

        var path = asked.Contains('/', StringComparison.Ordinal) ? asked : $"components/{asked}";
        var slug = path.Split('/')[1];
        // A Flux page with a slash is a page of its own, not a component's: Flux draws some things only in a DEMO
        // (`demo/account-overview`'s empty row), and the preparation marks there what is measured (empty-state).
        var fluxPage = prepared?.Flux(asked);
        var fluxUrl = $"https://fluxui.dev/{(fluxPage is { Length: > 0 } ? (fluxPage.Contains('/', StringComparison.Ordinal) ? fluxPage : $"components/{fluxPage}") : path)}";
        var output = Repo.Parity();
        // A layout page (layouts/header, layouts/sidebar) has no examples of its own: it links to full-document demos,
        // and each is measured whole — at desktop and phone widths, with the sidebar as it loads, narrowed to its rail
        // and slid over the page — against artifacts/flux-parity/rask/<slug>/<demo>.html.
        var layout = path.StartsWith("layouts/", StringComparison.Ordinal);
        // A viewport of its own is a measurement of its own, kept beside the desktop's.
        var sized = view is not null ? $"@{string.Join('x', view)}" : "";
        // …and a page of its own where the kit's page has to be drawn for that width (ChartPhoneParity: `chart@390x844.html`).
        var raskPage = Path.Combine(output, "rask", layout ? slug
            : File.Exists(Path.Combine(output, "rask", $"{slug}{sized}.html")) ? $"{slug}{sized}.html" : $"{slug}.html");
        if (!File.Exists(raskPage) && !Directory.Exists(raskPage))
        {
            Console.Error.WriteLine($"flux parity: {raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages");
            return 1;
        }

        // …and how the browser is started for it (parity-modal's reason: a reader's shows its scrollbars).
        var browser = await Browsers.Chromium(prepared?.Launch);
        var fluxFile = Path.Combine(output, "flux", slug, $"measurements{sized}.json");
        JsonObject flux;
        if (File.Exists(fluxFile) && !arg.Flag("refresh"))
        {
            flux = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(fluxFile))!;
        }
        else
        {
            flux = layout
                ? await Measure.MeasureLayouts(browser, await Measure.LayoutDemos(browser, $"https://fluxui.dev/{path}"), demo => $"https://fluxui.dev/demo/{demo.Name}", Path.Combine(output, "flux", slug))
                : await Measure.MeasurePage(browser, fluxUrl, Path.Combine(output, "flux", slug + sized),
                    prepared is null ? null : (p, scheme) => prepared.Prepare(p, scheme, "flux"), view);
            Directory.CreateDirectory(Path.Combine(output, "flux", slug));
            await File.WriteAllTextAsync(fluxFile, Js.Stringify(flux));
        }

        Task InheritInto(IPage page, string scheme) =>
            page.Run(Inherit, new JsonObject { ["examples"] = flux[scheme]!.DeepClone(), ["INHERITED"] = Js.ToNode(Inherited), ["FRAME"] = Js.ToNode(Frame), ["framed"] = view is not null });

        JsonObject rask;
        if (layout)
        {
            // A demo is told apart by the section it was measured under: `<h2 id>@<width>`, or its own name for the first.
            var demos = Example.Of(flux["light"]).Select(example => example.Section.Split('@')[0]).Distinct(StringComparer.Ordinal)
                .Select(key => new Measure.Demo(key, key)).ToList();
            rask = await Measure.MeasureLayouts(browser, demos, demo => Repo.FileUrl(Path.Combine(raskPage, $"{demo.Name}.html")), Path.Combine(output, "rask", slug),
                (page, scheme, _) => InheritInto(page, scheme), new HashSet<string>(Example.Of(flux["light"]).Select(example => example.Section), StringComparer.Ordinal));
        }
        else
        {
            rask = await Measure.MeasurePage(browser, Repo.FileUrl(raskPage), Path.Combine(output, "rask", slug + sized), prepared is null
                ? InheritInto
                : async (p, scheme) =>
                {
                    // The frame first: the runtime fits what it draws to the room it is given as it arrives (a composer's box to
                    // its text), and an app's page has its width from the start.
                    await InheritInto(p, scheme);
                    await Runtime.With(p, [.. prepared.Runtime]);
                    await prepared.Prepare(p, scheme, "rask");
                }, view);
        }

        await browser.CloseAsync();
        return Report(arg, slug, layout, flux, rask);
    }

    // ----- Layouts ---------------------------------------------------------------------------------------
    // What is not rendered is not compared — the menu of a closed dropdown, the label a rail hides — and each
    // state that shows it is measured on its own. Nor is what floats over the page (a `popover`: the tooltip of
    // the control that was just pressed), which is another component's. What Flux does in script Rask does with
    // an element (a checkbox, a label under the rail), marked `data-ui-mechanism` and left out.
    private static void Rendered(Example example)
    {
        var gone = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in example.Nodes)
        {
            if (node.Hidden || node.Has("popover") || node.Has("data-ui-mechanism") || (node.Parent is not null && gone.Contains(node.Parent)))
            {
                gone.Add(node.Id);
            }
        }

        example.Filter(node => !gone.Contains(node.Id));
    }

    private static int Report(Args arg, string slug, bool layout, JsonObject fluxJson, JsonObject raskJson)
    {
        var flux = new Dictionary<string, List<Example>>(StringComparer.Ordinal);
        var rask = new Dictionary<string, List<Example>>(StringComparer.Ordinal);
        foreach (var scheme in new[] { "light", "dark" })
        {
            flux[scheme] = Example.Of(fluxJson[scheme]?.DeepClone());
            rask[scheme] = Example.Of(raskJson[scheme]?.DeepClone());
            if (layout)
            {
                flux[scheme].ForEach(Rendered);
                rask[scheme].ForEach(Rendered);
            }
        }

        var comparer = new ParityComparer(slug, layout, ParityComparer.OwnParts(slug));
        var limit = arg.Flag("all") ? int.MaxValue : 12;
        var failures = 0;
        foreach (var scheme in new[] { "light", "dark" })
        {
            var sections = flux[scheme].GroupBy(example => example.Section, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            foreach (var mine in rask[scheme])
            {
                if (arg.Option("section") is { } only && mine.Section != only)
                {
                    continue;
                }

                var ordinal = mine.Ordinal ?? 0;
                var theirs = sections.TryGetValue(mine.Section, out var list) && ordinal < list.Count ? list[ordinal] : null;
                var label = $"{scheme} {mine.Section}#{ordinal}";
                if (theirs is null)
                {
                    Console.WriteLine($"?    {label}: Flux has no such example");
                    failures++;
                    continue;
                }

                var notes = new List<string>();
                var diffs = comparer.CompareExample(theirs, mine, notes);
                failures += diffs.Count > 0 ? 1 : 0;
                Console.WriteLine($"{(diffs.Count > 0 ? "FAIL" : "ok  ")} {label}{(diffs.Count > 0 ? $" — {diffs.Count} difference(s)" : "")}");
                foreach (var note in notes)
                {
                    Console.WriteLine($"       ({note})");
                }

                foreach (var d in diffs.Take(limit))
                {
                    Console.WriteLine($"       {d}");
                }

                if (diffs.Count > limit)
                {
                    Console.WriteLine($"       … {diffs.Count - limit} more (--all)");
                }
            }
        }

        Console.WriteLine(failures > 0 ? $"\nflux parity: {slug} differs in {failures} example(s)." : $"\nflux parity: {slug} matches Flux.");
        return failures > 0 ? 1 : 0;
    }
}
