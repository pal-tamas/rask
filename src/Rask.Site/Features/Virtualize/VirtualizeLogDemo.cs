using System.Globalization;
using Rask.Core.Virtualization;

namespace Rask.Site.Features;

// A log: lines of any length, so rows of any height, drawn between ctx.Above and ctx.Below — which is what measures
// them. Anchor: VirtualAnchor.End opens it at the last line and follows the lines added there while you are at the
// bottom; scroll up and it stays put. The scroller takes it to a line from code: the line lands at the top.
public sealed partial class VirtualizeLogDemo : Component
{
    private static readonly string[] Messages =
    [
        "GET /orders 200 in 12 ms",
        "Cache miss for catalogue:summer — rebuilt from the database in 84 ms, 1,204 products, 37 categories, stored for 10 minutes",
        "Signed in: ada@example.com",
        "Job SendInvoices finished: 48 sent, 2 waiting for a retry because the mail server answered 451 (try again later)",
        "POST /cart 201 in 31 ms",
    ];

    private readonly List<string> _lines = [.. Enumerable.Range(1, 500).Select(Line)];

    private readonly VirtualScroller _scroller = VirtualScroller.New();

    protected override Component? Render() =>
        Div.Class("space-y-2")[
            Virtualize.Items(
                ctx => Div
                    .Class("border rounded bg-white dark:bg-zinc-900 font-mono text-xs")
                    .Style("height:320px; overflow:auto;")
                    .Data("testid", "virtualize-log")
                    .OnScroll(ctx.OnScroll)[Lines(ctx)],
                _lines,
                ItemSize: 24,
                Anchor: VirtualAnchor.End,
                Scroller: _scroller),
            Div.Class("flex gap-2 flex-wrap")[
                Ui.Button.Id("virtualize-log-add").OnClick(Add)["Add 3 lines"],
                Ui.Button.Variant(Ui.ButtonVariant.Ghost).Id("virtualize-log-top").OnClick(() => _scroller.ScrollTo(0))["First line"],
                Span.Class("text-sm text-zinc-500 dark:text-white/70 self-center").Data("testid", "virtualize-log-count")[
                    _lines.Count.ToString(CultureInfo.InvariantCulture) + " lines"
                ]
            ]
        ];

    private static IEnumerable<Component> Lines(VirtualizationContext<string> ctx)
    {
        yield return ctx.Above;
        foreach (var line in ctx.VisibleItems)
        {
            yield return Div.Key(line.Index).Class("px-3 py-1 border-b border-zinc-100 dark:border-white/10").Data("row", line.Index.ToString(CultureInfo.InvariantCulture))[line.Value];
        }

        yield return ctx.Below;
    }

    private void Add()
    {
        for (var i = 0; i < 3; i++)
        {
            _lines.Add(Line(_lines.Count + 1));
        }
    }

    private static string Line(int n) =>
        "[" + n.ToString("0000", CultureInfo.InvariantCulture) + "] " + Messages[n % Messages.Length];
}
