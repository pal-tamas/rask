using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Rask.Flux.Commands;

/// <summary>
///     Has Flux UI changed since Rask.Ui last matched it? Answers without anyone asking.
/// </summary>
/// <remarks>
///     Three things can move upstream, and each is checked against what is committed: the CATALOGUE — a component, prop
///     or value added, removed or renamed (flux.snapshot.json); the LOOK — any example on any docs page measuring
///     differently (flux.lock.json); the RELEASE — the version Flux publishes (flux.lock.json).
///     <para>
///         It rewrites both files and prints what moved, so <c>git diff</c> is the report and the next steps are the usual
///         ones: FluxConformanceTests names the props Rask.Ui now lacks, and <c>flux parity &lt;slug&gt;</c> names the
///         pixels. The fresh measurements stay in artifacts/flux-parity/flux/, where parity reads them.
///     </para>
///     <para>
///         A look is measured in pixels and text measures differently on every OS, so the lock belongs to ONE platform:
///         <c>measuredOn</c>, the CI runner's. Anywhere else the looks are measured but neither compared nor locked.
///         <c>--baseline</c> takes the lock over: every look is locked as measured here, none reported. It measures each
///         page twice, and an example Flux draws differently each time (a chart's data is made up per request) is locked as
///         <c>unstable</c>: never compared, never reported.
///     </para>
///     <code>
///     flux sync              # everything
///     flux sync button card  # these pages only (the lock keeps the others)
///     flux sync --baseline   # lock every look as this platform measures it
///     flux sync --comparable # exit 0 when the lock was taken here, by this code
///     </code>
///     Exit: 0 nothing moved · 2 something did · 1 it could not tell.
/// </remarks>
[FluxCommand("sync", "has Flux's catalogue, look or release moved since the lock?")]
public sealed partial class Sync : IFluxCommand
{
    private const string Unstable = "unstable";

    /// <summary>
    ///     The code a print is taken with. A print is only as good as the code that took it: a change to how a page is
    ///     measured moves every look it touches, and that is not Flux moving. The lock names the code, and is retaken when
    ///     it changes.
    /// </summary>
    private static readonly string[] MeasuringCode =
    [
        "Lib/Measure.cs", "Lib/PageScripts.cs", "Lib/Pressables.cs", "Lib/Screen.cs", "Lib/Js.cs", "Lib/Eval.cs", "Commands/Sync.cs",
    ];

    private readonly List<string> detail = [];  // every example behind those pages — printed above the report, for the log

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var flux = Repo.Path("tests", "Rask.Ui.Tests", "Flux");
        var snapshotFile = Path.Combine(flux, "flux.snapshot.json");
        var lockFile = Path.Combine(flux, "flux.lock.json");
        var measuredWith = MeasuredWith();
        var baseline = args.Contains("--baseline");
        var only = args.Where(arg => !arg.StartsWith("--", StringComparison.Ordinal)).ToList();
        if (baseline && only.Count > 0)
        {
            Console.Error.WriteLine("flux sync: --baseline locks every page on this platform, so it takes no page filter.");
            return 1;
        }

        var lockJson = File.Exists(lockFile) ? (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(lockFile))! : new JsonObject { ["release"] = "", ["pages"] = new JsonObject() };
        var lockedOn = Js.Str(lockJson["measuredOn"]) ?? "another platform";
        var comparable = Js.Str(lockJson["measuredOn"]) == Repo.Platform && Js.Str(lockJson["measuredWith"]) == measuredWith;
        if (args.Contains("--comparable"))
        {
            return comparable ? 0 : 1;
        }

        var compare = !baseline && comparable;
        var before = File.Exists(snapshotFile) ? await File.ReadAllTextAsync(snapshotFile) : "";
        var refreshed = await new Refresh().Run([]);
        if (refreshed != 0)
        {
            return 1;
        }

        var snapshot = JsonNode.Parse(await File.ReadAllTextAsync(snapshotFile))!;
        var moved = new List<string>();   // the report: a line per catalogue or release move, and ONE per page whose look moved
        if (before.Length > 0 && before != Js.Stringify(snapshot, 2) + "\n")
        {
            moved.AddRange(CatalogueChanges(JsonNode.Parse(before)!, snapshot));
        }

        var release = await LatestRelease();
        if (release.Length > 0 && release != (Js.Str(lockJson["release"]) ?? ""))
        {
            if (Js.Str(lockJson["release"]) is { Length: > 0 } was)
            {
                moved.Add($"release: {was} -> {release}");
            }

            lockJson["release"] = release;
        }

        if (baseline)
        {
            lockJson["pages"] = new JsonObject();
        }

        var pages = (JsonObject)(lockJson["pages"] ??= new JsonObject());
        var browser = await Browsers.Chromium();
        foreach (var page in (snapshot["pages"] as JsonArray ?? []).Where(p => only.Count == 0 || only.Contains(Js.Str(p!["slug"])!)))
        {
            var slug = Js.Str(page!["slug"])!;
            var known = compare ? pages[slug] as JsonObject : null;
            var prints = await Fingerprint(browser, page, known);
            // Moved means REPRODUCIBLY moved: Flux draws some examples at random, so a difference is measured twice.
            if (baseline)
            {
                prints = Twice(slug, prints, await Fingerprint(browser, page, null), _ => Unstable);
            }
            else if (known is not null && !Alike(known, prints))
            {
                prints = Twice(slug, prints, await Fingerprint(browser, page, known), key => Js.Str(known[key]));
            }

            if (known is not null)
            {
                moved.AddRange(LookChanges(slug, known, prints));
            }

            if (compare || baseline)
            {
                pages[slug] = Prints(prints);
            }

            Console.WriteLine($"flux sync: measured {slug} ({Js.Num(prints.Count / 2.0)} examples)");
        }

        await browser.CloseAsync();
        if (baseline)
        {
            lockJson["measuredOn"] = Repo.Platform;
            lockJson["measuredWith"] = measuredWith;
        }

        var written = new JsonObject { ["release"] = lockJson["release"]?.DeepClone() };
        if (lockJson["measuredOn"] is { } on)
        {
            written["measuredOn"] = on.DeepClone();
        }

        if (lockJson["measuredWith"] is { } with)
        {
            written["measuredWith"] = with.DeepClone();
        }

        written["pages"] = pages.DeepClone();
        await File.WriteAllTextAsync(lockFile, Js.Stringify(written, 1) + "\n");

        if (detail.Count > 0)
        {
            Console.WriteLine($"\nflux sync: every example that moved:\n  {string.Join("\n  ", detail)}");
        }

        if (baseline)
        {
            Console.WriteLine($"\nflux sync: looks baselined on {Repo.Platform} (were locked on {lockedOn}).");
        }
        else if (!compare)
        {
            Console.WriteLine($"\nflux sync: looks are locked on {lockedOn}{(Js.Str(lockJson["measuredOn"]) == Repo.Platform ? " by other measuring code" : $"; this is {Repo.Platform}")}, so they were measured but not compared.");
        }

        Console.WriteLine(moved.Count > 0
            ? $"\nflux sync: Flux moved in {moved.Count} place(s):\n  {string.Join("\n  ", moved)}"
            : $"\nflux sync: {(compare ? "Flux is where Rask.Ui last matched it" : "the catalogue and the release are where Rask.Ui last matched them")}.");
        return moved.Count > 0 ? 2 : 0;
    }

    private static string MeasuredWith()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in MeasuringCode)
        {
            hash.AppendData(File.ReadAllBytes(Repo.Path(["scripts", "flux", "Flux", .. file.Split('/')])));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset())[..16];
    }

    private static JsonObject Prints(Dictionary<string, string> prints)
    {
        var json = new JsonObject();
        foreach (var (key, value) in prints)
        {
            json[key] = value;
        }

        return json;
    }

    // Measures a page, leaves what parity reads, and hands back its fingerprints — an example the
    // lock knows as unstable keeps that name, so it compares equal whatever it measured this time.
    private static async Task<Dictionary<string, string>> Fingerprint(IBrowser browser, JsonNode page, JsonObject? known)
    {
        var (kind, slug) = (Js.Str(page["kind"])!, Js.Str(page["slug"])!);
        var dir = Repo.Parity("flux", slug);
        var url = $"https://fluxui.dev/{kind}/{slug}";
        // A layout's examples are the demos its page links to, each at every width and state (Measure).
        async Task<JsonObject> Take() => kind == "layouts"
            ? await Measure.MeasureLayouts(browser, await Measure.LayoutDemos(browser, url), demo => $"https://fluxui.dev/demo/{demo.Name}", dir)
            : await Measure.MeasurePage(browser, url, dir);
        JsonObject schemes;
        try
        {
            schemes = await Take();
        }
        catch (Exception error) when (error is not FluxExit)
        {
            // One more try: a docs page that never went idle once is the network, not Flux.
            schemes = await Take();
        }

        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "measurements.json"), Js.Stringify(schemes));
        var prints = Fingerprints(schemes);
        foreach (var key in prints.Keys.ToList())
        {
            if (Js.Str(known?[key]) == Unstable)
            {
                prints[key] = Unstable;
            }
        }

        return prints;
    }

    private static bool Alike(JsonObject a, Dictionary<string, string> b) =>
        a.Select(p => p.Key).Concat(b.Keys).All(key => Js.Str(a[key]) == b.GetValueOrDefault(key));

    // Two fresh measurements of one page: where they agree, that is the look; where they do not, the
    // example is unstable and is never reported — a compare keeps what the lock holds (`otherwise`), a
    // baseline locks it as unstable.
    private static Dictionary<string, string> Twice(string slug, Dictionary<string, string> first, Dictionary<string, string> second, Func<string, string?> otherwise)
    {
        var prints = new Dictionary<string, string>(StringComparer.Ordinal);
        var keys = first.Keys.Concat(second.Keys).Distinct(StringComparer.Ordinal).ToList();
        static string ExampleOf(string key) => key[(key.IndexOf('/', StringComparison.Ordinal) + 1)..];   // light and dark are one example
        var unstable = keys.Where(key => first.GetValueOrDefault(key) != second.GetValueOrDefault(key)).Select(ExampleOf).Distinct(StringComparer.Ordinal).ToList();
        foreach (var key in keys)
        {
            var print = unstable.Contains(ExampleOf(key)) ? otherwise(key) : first.GetValueOrDefault(key);
            if (!string.IsNullOrEmpty(print))
            {
                prints[key] = print;
            }
        }

        if (unstable.Count > 0)
        {
            Console.WriteLine($"flux sync: {slug} {string.Join(", ", unstable)} does not measure the same twice; ignored");
        }

        return prints;
    }

    // One line per page for the report; the examples behind it go to the log.
    private List<string> LookChanges(string slug, JsonObject known, Dictionary<string, string> prints)
    {
        var counts = new (string How, List<string> Examples)[] { ("changed", []), ("new", []), ("gone", []) };
        foreach (var key in known.Select(p => p.Key).Concat(prints.Keys).Distinct(StringComparer.Ordinal))
        {
            var (was, now) = (Js.Str(known[key]), prints.GetValueOrDefault(key));
            if (was == now)
            {
                continue;
            }

            var how = string.IsNullOrEmpty(was) ? "new" : string.IsNullOrEmpty(now) ? "gone" : "changed";
            var examples = counts.First(c => c.How == how).Examples;
            var example = key[(key.IndexOf('/', StringComparison.Ordinal) + 1)..];   // light and dark are one example
            if (!examples.Contains(example))
            {
                examples.Add(example);
            }

            detail.Add($"look: {slug} {key} {(how == "changed" ? how : $"is {how}")}");
        }

        var summary = counts.Where(c => c.Examples.Count > 0).Select(c => $"{c.Examples.Count} {c.How}").ToList();
        return summary.Count > 0 ? [$"look: {slug} — example(s): {string.Join(", ", summary)}"] : [];
    }

    /// <summary>One short hash per example and scheme: enough to say WHICH example moved; parity says how.</summary>
    public static Dictionary<string, string> Fingerprints(JsonObject schemes)
    {
        var prints = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (scheme, examples) in schemes)
        {
            foreach (var example in (examples as JsonArray ?? []).OfType<JsonObject>())
            {
                static JsonNode? Round(JsonNode? v) => Js.Str(v) is { } s ? JsonValue.Create(Hundredths().Replace(s, m => Js.Num(Js.Round(Js.Parse(m.Value) * 100) / 100))) : v?.DeepClone();
                static JsonArray Values(JsonObject? o) => new([.. (o ?? []).Select(p => Round(p.Value))]);
                var facts = new JsonArray();
                foreach (var n in (example["nodes"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    var fact = new JsonArray
                    {
                        n["tag"]?.DeepClone(),
                        new JsonArray([.. (n["attrs"] as JsonObject ?? []).Select(a => a.Key).Where(a => a.StartsWith("data-flux", StringComparison.Ordinal)).Order(StringComparer.Ordinal).Select(a => (JsonNode?)a)]),
                        n["text"]?.DeepClone(),
                        new JsonArray([.. (n["box"] as JsonArray ?? []).Skip(2).Select(v => (JsonNode?)Js.Round(Js.Number(v)))]),
                        Values(n["style"] as JsonObject),
                        n["::before"] is JsonObject before ? Values(before) : (JsonNode)0,
                        n["::after"] is JsonObject after ? Values(after) : (JsonNode)0,
                    };
                    // What moves and how, only where something does: an example with nothing moving keeps its print.
                    if (n["animations"] is { } animations)
                    {
                        fact.Add(animations.DeepClone());
                    }

                    facts.Add(fact);
                }

                var states = new JsonArray([.. (example["states"] as JsonArray ?? []).OfType<JsonObject>().Select(s => (JsonNode?)new JsonArray
                {
                    s["state"]?.DeepClone(),
                    new JsonArray([.. (s["changed"] as JsonObject ?? []).Select(c => (JsonNode?)new JsonArray { c.Key, Round(c.Value) })]),
                })]);
                var section = Js.Str(example["section"]) is { Length: > 0 } named ? named : "intro";
                var text = Js.Stringify(new JsonArray { facts, states });
                prints[$"{scheme}/{section}#{Js.Num(Js.Number(example["ordinal"]))}"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
            }
        }

        return prints;
    }

    private static IEnumerable<string> CatalogueChanges(JsonNode old, JsonNode now)
    {
        static List<(string Key, string Value)> Flat(JsonNode snap) => (snap["pages"] as JsonArray ?? []).SelectMany(p => (p!["parts"] as JsonArray ?? []).OfType<JsonObject>().SelectMany(part =>
            new[] { (Js.Str(part["name"])!, "") }.Concat(part.Where(entry => entry.Key != "name").SelectMany(entry =>
                (entry.Value as JsonArray ?? []).OfType<JsonObject>().Select(e => ($"{Js.Str(part["name"])} {entry.Key} {Js.Str(e["name"])}",
                    Js.Stringify(new JsonArray { e["options"]?.DeepClone() ?? new JsonArray(), e["default"]?.DeepClone() ?? "" }))))))).ToList();
        static Dictionary<string, string> AsMap(List<(string Key, string Value)> list)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in list)
            {
                map[key] = value;   // a later entry of the same key wins, as in a Map built from pairs
            }

            return map;
        }

        var (a, b) = (AsMap(Flat(old)), Flat(now));
        var bMap = AsMap(b);
        var changes = new List<string>();
        foreach (var key in b.Select(e => e.Key).Distinct(StringComparer.Ordinal))
        {
            if (!a.TryGetValue(key, out var was))
            {
                changes.Add($"catalogue: + {key}");
            }
            else if (was != bMap[key])
            {
                changes.Add($"catalogue: ~ {key} {was} -> {bMap[key]}");
            }
        }

        changes.AddRange(Flat(old).Select(e => e.Key).Distinct(StringComparer.Ordinal).Where(key => !bMap.ContainsKey(key)).Select(key => $"catalogue: - {key}"));
        return changes;
    }

    // Release METADATA only — the tag name. The repository's source is proprietary and is never read.
    private static async Task<string> LatestRelease()
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("rask-flux-sync");
            using var response = await http.GetAsync(new Uri("https://api.github.com/repos/livewire/flux/releases/latest"));
            return response.IsSuccessStatusCode ? Js.Str(JsonNode.Parse(await response.Content.ReadAsStringAsync())?["tag_name"]) ?? "" : "";
        }
        catch (HttpRequestException)
        {
            return "";
        }
        catch (TaskCanceledException)
        {
            return "";
        }
    }

    [GeneratedRegex("-?[0-9]*\\.[0-9]+(e-?[0-9]+)?")]
    private static partial Regex Hundredths();
}
