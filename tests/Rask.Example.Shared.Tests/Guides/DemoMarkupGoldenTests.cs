using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Rask.Example.Shared;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Guides;

// A committed snapshot of the *markup skeleton* of every demo in DemoRegistry: each element's tag and its
// CSS classes, in document order. Nothing else asserts this. The Bs* unit tests cover components in
// isolation, and the E2E journey only asserts structure loosely (".list-group-item is visible"), so a
// refactor that silently restyles or re-nests a demo passes both — while the demos ARE the teaching
// surface (CodeSample embeds their real source into the guides), so their markup is what readers copy.
//
// It snapshots the skeleton rather than the raw HTML for a hard reason: raw HTML is not reproducible here.
// Rendering a demo twice in one process yields different bytes every time — ElementRef mints a fresh Guid
// per instance (data-rask-ref), model seeds use Guid.NewGuid() (data-rask-key), the radio/checkbox group
// and gesture-bridge ids come off process-global counters (radio-group-3 vs radio-group-5), and the
// lifecycle ticker stamps a clock. The counters are the decisive ones: they depend on what else ran in the
// process, so raw-HTML goldens would be order-dependent across an unordered xUnit run — flaky by
// construction, not merely noisy.
//
// Every one of those moving parts lives in an id, a data-* attribute, or text — never in a class attribute
// or a tag name. So the skeleton is exactly the reproducible part, and it is also precisely the part that
// carries CSS meaning. Class tokens are sorted because their order in the attribute has no effect on CSS
// whatsoever: a pure reordering is correctly a non-diff, while a token being added or removed is a diff.
// That makes "reordering is fine, add/remove is not" mechanical instead of a judgement call on a diff.
//
// What it deliberately does NOT guard: attribute values, text content, and handler wiring. Those have
// their own tests.
//
// To regenerate after an intended change:
//     RASK_UPDATE_GOLDEN=1 dotnet test tests/Rask.Example.Shared.Tests --filter FullyQualifiedName~DemoMarkupGolden
// then read `git diff` on the .golden.txt before committing. That diff IS the review.
public sealed class DemoMarkupGoldenTests
{
    // Opening tags. Attribute values are HTML-encoded by the serializer, so no raw '>' can appear inside
    // one and this can't run past the tag.
    private static readonly Regex Tags = new(
        """<(?<tag>[a-zA-Z][a-zA-Z0-9-]*)(?<attrs>(?:"[^"]*"|[^>"])*)>""", RegexOptions.Compiled);

    private static readonly Regex ClassAttr = new("\\sclass=\"(?<v>[^\"]*)\"", RegexOptions.Compiled);

    // The body of a CodeSample's syntax-highlighted source listing. Dropped before the skeleton is taken,
    // keeping the <code> element itself. ColorCode turns the source into one <span class="keyword|string|…">
    // per token, so a demo's listing contributes hundreds of spans that restructure whenever its source
    // changes — including when only a comment does. That is pure noise here: the tokens display source
    // text, they carry no layout. Worse, it is noise precisely when this file matters most, since the
    // change most likely to need reviewing (editing a demo) is exactly the one that would swamp the diff.
    private static readonly Regex HighlightedSource = new(
        "(<code[^>]*class=\"[^\"]*language-[^\"]*\"[^>]*>).*?</code>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // The second legitimate exception, and the counterpart to the one below: a chart's *contents* are its
    // data. LiveTicker polls on a 50 ms simulated latency, so the Sparkline it draws holds no points on the
    // first render and one shortly after — and an empty series renders a labelled <text>No data</text>
    // frame where a populated one renders <line>/<polyline>/<circle>. That is a tag-name change on a timer,
    // which no snapshot can hold; it made this golden a race against machine load and failed unrelated PRs
    // (#618). The <svg> element itself is kept — LiveTicker always emits it, so "is the chart there, in the
    // right place, with the right classes" is still asserted — and only its body is dropped. What the chart
    // draws is Sparkline's contract and has its own tests in Rask.Example.Shared.Tests.
    private static readonly Regex LiveChartBody = new(
        "(<svg[^>]*class=\"[^\"]*ticker-chart-svg[^\"]*\"[^>]*>).*?</svg>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void EveryDemo_RendersToItsGoldenMarkupSkeleton()
    {
        var actual = RenderAll();
        var path = GoldenPath();

        if (Environment.GetEnvironmentVariable("RASK_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path),
            $"Golden file missing: {path}\nRegenerate with RASK_UPDATE_GOLDEN=1 dotnet test …");

        Assert.Equal(File.ReadAllText(path), actual);
    }

    // The guard on the guard: if a demo's skeleton is not reproducible, its golden entry would fail on
    // someone else's unrelated PR and train everyone to regenerate without reading the diff. Catch that
    // here, where the cause is still obvious.
    [Fact]
    public void EveryDemoSkeleton_IsReproducible()
    {
        var offenders = DemoRegistry.Keys
            .Where(k => !string.Equals(Skeleton(k), Skeleton(k), StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These demos' markup skeletons differ across two consecutive renders, so they can't be "
            + "snapshotted. A moving part has leaked into a tag name or a class attribute:\n  "
            + string.Join("\n  ", offenders));
    }

    // The half the pair above cannot see, and the half #618 actually needed. Two back-to-back renders each
    // mount a FRESH instance and capture it immediately, so both land on the same side of any mount-time
    // timer and agree — which is exactly why LiveTicker's 50 ms simulated poll latency walked straight
    // through that check and failed the golden instead, on whichever unrelated PR happened to run on a busy
    // machine.
    //
    // So hold one instance and read it again after its timers have had time to fire. A demo may of course
    // CHANGE when it ticks — that is the point of a live demo — but the change has to live in text, an id
    // or a data-* attribute, which is the contract this file has always stated. A tag name or a class that
    // moves on a timer cannot be snapshotted by anyone.
    [Fact]
    public async Task NoDemoSkeleton_ChangesOnATimer()
    {
        var offenders = new List<string>();

        foreach (var key in DemoRegistry.Keys)
        {
            var page = RaskTest.Render(() => DemoRegistry.Build(key), TestServices.Default());
            var before = SkeletonOf(page.Html);

            // Comfortably past LiveTicker's 50 ms poll and LiveTickerDemo's 50 ms deferred re-render — the
            // two mount-time timers in the set — without waiting on the 1 s inter-tick interval.
            await Task.Delay(250);

            var after = SkeletonOf(page.Render());
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                offenders.Add($"{key}: {FirstDifference(before, after)}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These demos' markup skeletons changed on their own after mount, so their golden entry is a "
            + "race against the wall clock. Move the moving part into text, an id or a data-* attribute — "
            + "never a tag name or a class:\n  " + string.Join("\n  ", offenders));
    }

    private static string RenderAll()
    {
        var sb = new StringBuilder();

        // Ordinal sort so file order is stable regardless of DemoRegistry's declaration order — moving a
        // demo within DemoRegistry.cs must not show up as a diff here.
        foreach (var key in DemoRegistry.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            sb.Append("=== ").Append(key).Append(" ===\n").Append(Skeleton(key)).Append('\n');
        }

        return sb.ToString();
    }

    // "lifecycle-ticker" on its own sends you reading a 300-line component; naming the line that moved
    // sends you to the element.
    private static string FirstDifference(string before, string after)
    {
        var a = before.Split('\n');
        var b = after.Split('\n');

        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var left = i < a.Length ? a[i] : "(end)";
            var right = i < b.Length ? b[i] : "(end)";
            if (!string.Equals(left, right, StringComparison.Ordinal))
            {
                return $"line {i + 1}: `{left}` became `{right}`";
            }
        }

        return "(no line differs — trailing whitespace?)";
    }

    private static string Skeleton(string key) =>
        SkeletonOf(RaskTest.Render(() => DemoRegistry.Build(key), TestServices.Default()).Html);

    private static string SkeletonOf(string rendered)
    {
        var html = LiveChartBody.Replace(HighlightedSource.Replace(rendered, "$1</code>"), "$1</svg>");

        var sb = new StringBuilder();

        foreach (Match tag in Tags.Matches(html))
        {
            sb.Append(tag.Groups["tag"].Value);

            var cls = ClassAttr.Match(tag.Groups["attrs"].Value);
            if (cls.Success)
            {
                var tokens = cls.Groups["v"].Value
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();

                foreach (var token in tokens.OrderBy(t => t, StringComparer.Ordinal))
                {
                    sb.Append(" .").Append(token);
                }
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    // The golden lives next to this source file. Resolving it from the compiler rather than copying it to
    // the output directory keeps the csproj untouched and lets RASK_UPDATE_GOLDEN rewrite the real file.
    private static string GoldenPath([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "DemoMarkup.golden.txt");
}
