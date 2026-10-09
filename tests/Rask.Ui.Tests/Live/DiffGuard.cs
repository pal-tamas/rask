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
///     Every state change any test here drives is held to what a live session may answer it with: a diff. A
///     change the session's gate would refuse — and answer with the whole page — fails the test that drove it.
/// </summary>
/// <remarks>
///     A whole page for a keypress is tens of kilobytes where a diff is a few hundred bytes, and it lands on
///     top of whatever the reader typed since. So no kit component may change shape in a way the differ cannot
///     follow: a child that comes and goes, or changes element, is keyed (see
///     <c>docs/building-components.md</c>). <see cref="Known" /> names what is not fixed yet.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class DiffGuardAttribute : BeforeAfterTestAttribute
{
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<string>> Refused = new(StringComparer.Ordinal);

    /// <summary>
    ///     What still drives a change the gate refuses — a test class, or one test of one — and where the shape
    ///     changes. Nothing here is forgiven quietly: <c>KitStateChangesAreDiffsTests</c> fails the day one of its
    ///     own stops being refused, and the other is a component the Flux rebuild has not reached.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> Known = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // daisy-drawn, not rebuilt from Flux yet: a row that does not match the query is not rendered, and the
        // rows are the page's own children, which nothing keys.
        ["UiCommandTests"] = "src/Rask.Ui/UiCommandRows.cs:26",
        ["KitStateChangesAreDiffsTests.A_prop_change_the_gate_still_refuses_is_listed_until_it_is_fixed"] = "KitStateChangesAreDiffsTests.Refused",
        ["KitStateChangesAreDiffsTests.A_toast_is_answered_with_a_diff_where_its_outlet_is_the_last_child_and_the_whole_page_where_it_is_not"] = "src/Rask.Ui/UiToast.Messages.cs:48",
    };

    [ModuleInitializer]
    internal static void WatchEveryPage() => Page.Watching = static () => new Watch(TestContext.Current.Test?.UniqueID);

    /// <summary>The changes the running test has driven so far that a session would answer with the whole page.</summary>
    internal static IReadOnlyCollection<string> FullPages() =>
        TestContext.Current.Test?.UniqueID is { } id && Refused.TryGetValue(id, out var refused) ? refused : [];

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        var type = methodUnderTest.DeclaringType!.Name;
        if (!Refused.TryRemove(test.UniqueID, out var refused) || Known.ContainsKey(type) || Known.ContainsKey(type + "." + methodUnderTest.Name))
        {
            return;
        }

        Assert.Fail(
            "A state change this test drove would be answered with the WHOLE PAGE instead of a diff. Key the child "
            + "that comes and goes or changes element:\n  " + string.Join("\n  ", refused.Distinct(StringComparer.Ordinal)));
    }

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

            if (_cache.LastDiffForcedFullHtml)
            {
                Refused.GetOrAdd(test, static _ => new ConcurrentQueue<string>()).Enqueue("raw markup beside siblings at the document's own level");
            }
        }

        private static string Describe(EditOp op, string html) =>
            op.Kind + " at " + string.Join('/', op.Path.Skip(1))
            + (op.HtmlStart >= 0 ? " " + Cut(html[op.HtmlStart..op.HtmlEnd]) : string.Empty);

        private static string Cut(string markup) => markup.Length <= 120 ? markup : markup[..120] + "…";
    }
}
