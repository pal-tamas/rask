using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Rask.Core.Live;
using Rask.Testing;
using Xunit.v3;
using Page = Rask.Testing.Page;

[assembly: Rask.UiTests.Live.DiffGuard]

namespace Rask.UiTests.Live;

/// <summary>
///     Every state change any test here drives is held to what a live session may answer it with: a diff that
///     names the nodes that changed. A change answered with the whole page fails the test that drove it, and so
///     does one answered by sending a parent's children again as markup.
/// </summary>
/// <remarks>
///     A whole page for a keypress is tens of kilobytes where a diff is a few hundred bytes, and it lands on
///     top of whatever the reader typed since. A child that changes element is no longer a whole page — the
///     differ morphs the parent it sits in — but that parent's children still travel whole and are paired by
///     position when they land, so it is not what a kit component should cost either. So no kit component may
///     change shape in a way the differ cannot follow node by node: one child that comes or goes among
///     siblings that still match is followed as it is, and a child that changes element, or several that come
///     and go apart from each other, are keyed (see <c>docs/building-components.md</c>). <see cref="KnownMorphs" />
///     names what is not fixed yet, and the test it names holds each to the parent and the bytes it has today.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class DiffGuardAttribute : BeforeAfterTestAttribute
{
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<string>> Refused = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, ConcurrentQueue<MorphedParent>> Morphed = new(StringComparer.Ordinal);

    /// <summary>
    ///     What still drives a change the differ answers with a morph of a parent — a test class, or one test of
    ///     one — and where. A WHOLE PAGE is forgiven nowhere. Nothing here is forgiven quietly either:
    ///     <c>KitStateChangesAreDiffsTests</c> holds each of its own to the one parent it morphs and the bytes
    ///     that costs, and fails the day one becomes a diff of the nodes that changed.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> KnownMorphs = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["KitStateChangesAreDiffsTests.A_prop_change_that_puts_one_node_in_the_place_of_another_morphs_one_parent_within_its_bytes"] =
            "KitStateChangesAreDiffsTests.Morphs",
        ["UiChartTests"] = ChartIsRawSvg,
        ["FluxParityPages.Every_parity_page_is_written_for_the_browser_to_measure"] = ChartIsRawSvg,
        ["OnePositionPerElementTests.No_element_on_a_parity_page_asks_for_two_positions_at_once"] = ChartIsRawSvg,
    };

    private const string ChartIsRawSvg =
        "src/Rask.Ui/UiChartSvg.cs:80 — a drawing is raw SVG beside its hover layer, so a chart drawn again is one morph of the chart";

    [ModuleInitializer]
    internal static void WatchEveryPage() => Page.Watching = static () => new Watch(TestContext.Current.Test?.UniqueID);

    /// <summary>The changes the running test has driven so far that a session would answer with the whole page.</summary>
    internal static IReadOnlyCollection<string> FullPages() =>
        TestContext.Current.Test?.UniqueID is { } id && Refused.TryGetValue(id, out var refused) ? refused : [];

    /// <summary>The parents whose children the running test's changes sent again as markup, in the order they went.</summary>
    internal static IReadOnlyCollection<MorphedParent> Morphs() =>
        TestContext.Current.Test?.UniqueID is { } id && Morphed.TryGetValue(id, out var morphed) ? morphed : [];

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        var type = methodUnderTest.DeclaringType!.Name;
        var wholePages = Refused.TryRemove(test.UniqueID, out var refused);
        var morphs = Morphed.TryRemove(test.UniqueID, out var morphed)
                     && !KnownMorphs.ContainsKey(type) && !KnownMorphs.ContainsKey(type + "." + methodUnderTest.Name);
        if (wholePages)
        {
            Assert.Fail(
                "A state change this test drove would be answered with the WHOLE PAGE instead of a diff. Key the child "
                + "that comes and goes or changes element:\n  " + string.Join("\n  ", refused!.Distinct(StringComparer.Ordinal)));
        }

        if (morphs)
        {
            Assert.Fail(
                "A state change this test drove would be answered by sending a parent's children again as markup, "
                + "paired by position when they land, instead of a diff of the nodes that changed. Key the child that "
                + "changes element, and its siblings:\n  "
                + string.Join("\n  ", morphed!.Select(static m => m.ToString()).Distinct(StringComparer.Ordinal)));
        }
    }

    /// <summary>A parent whose children went out as markup: where it is below the body, and that markup.</summary>
    internal readonly record struct MorphedParent(string Path, string Markup)
    {
        public override string ToString() => $"the children of {Path} ({Markup.Length} chars) {Cut(Markup)}";
    }

    private static string Cut(string markup) => markup.Length <= 120 ? markup : markup[..120] + "…";

    // One page's renders. The walk is wrapped in a <body> frame, as a real page's is: the differ trusts an
    // append or a truncation only below the document's own level, and a component under test would be at it.
    private sealed class Watch(string? test) : IRenderWatch
    {
        private readonly SessionRenderCache _cache = new();
        private readonly List<EditOp> _ops = [];
        private FrameWriter? _frames;
        private int _body;

        public FrameWriter Frames()
        {
            _frames = _cache.PrepareCurrentBuffer();
            _body = _frames.OpenElement("body", null, false, 0);

            return _frames;
        }

        public void Rendered(string html)
        {
            _frames!.CloseElement(_body, html.Length);
            if (!_cache.TryComputeDiff(_ops, html) || test is null)
            {
                return;
            }

            foreach (var op in _ops.Where(op => !LiveDiffGate.DiffOpsAreClientSupported([op])))
            {
                Refused.GetOrAdd(test, static _ => new ConcurrentQueue<string>()).Enqueue(Describe(op, html));
            }

            foreach (var op in _ops.Where(static op => op.Kind == EditOpKind.MorphSubtree))
            {
                Morphed.GetOrAdd(test, static _ => new ConcurrentQueue<MorphedParent>())
                    .Enqueue(new MorphedParent(string.Join('/', op.Path.Skip(1)), op.Value ?? html[op.HtmlStart..op.HtmlEnd]));
            }

            if (_cache.LastDiffForcedFullHtml)
            {
                Refused.GetOrAdd(test, static _ => new ConcurrentQueue<string>()).Enqueue("raw markup beside siblings at the document's own level");
            }
        }

        private static string Describe(EditOp op, string html) =>
            op.Kind + " at " + string.Join('/', op.Path.Skip(1))
            + (op.HtmlStart >= 0 ? " " + Cut(html[op.HtmlStart..op.HtmlEnd]) : string.Empty);
    }
}
