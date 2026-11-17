using System.Globalization;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Virtualization;

#pragma warning disable RASK019 // a test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     Three measured virtual lists whose rows are one to four lines tall, 28 to 88 pixels against the 30 the first is
///     told: one that opens at a row and scrolls to another from a button, a log anchored to its tail that a button adds
///     to, and a table, whose rows the runtime holds itself. Served by the Server host (<c>VirtualListTests</c>) and,
///     linked into the WASM fixture, by the browser host (<c>Rask.Wasm.E2E.Tests</c>), so both are driven alike
///     (<c>VirtualListJourneys</c>).
/// </summary>
public sealed partial class VirtualListPage : Component
{
    internal const int Rows = 3000;

    internal const int Initial = 1234;

    internal const int Target = 2345;

    internal const int FirstLog = 400;

    internal const int Added = 5;

    private static readonly List<int> _rows = [.. Enumerable.Range(0, Rows)];

    private readonly List<int> _log = [.. Enumerable.Range(0, FirstLog)];

    private readonly VirtualScroller _scroller = VirtualScroller.New();

    private string _landed = "none";

    /// <summary>How many lines row <paramref name="row" /> holds: 1 to 4, mixed so no two neighbours agree for long.</summary>
    internal static int Lines(int row) => 1 + (row * 7 % 4);

    private const string Box = "height:320px;overflow:auto;border:1px solid #ccc;position:relative;";

    private const string Row = "box-sizing:border-box;padding:4px 8px;line-height:20px;border-bottom:1px solid #eee;";

    protected override Component? Render() =>
        Div.Style("display:grid;gap:12px;padding:12px;max-width:900px;")[
            Virtualize.Items(
                ctx => Div.Id("mixed-box").Style(Box).OnScroll(ctx.OnScroll)[MixedRows(ctx)],
                _rows,
                ItemSize: 30,
                InitialClientHeight: 320,
                InitialItemIndex: Initial,
                Scroller: _scroller),
            Button.Id("go").OnClick(Go)["go"],
            P.Id("landed")[_landed],
            Virtualize.Items(
                ctx => Div.Id("log-box").Style(Box).OnScroll(ctx.OnScroll)[LogRows(ctx)],
                _log,
                ItemSize: 30,
                InitialClientHeight: 320,
                Anchor: VirtualAnchor.End),
            Button.Id("append").OnClick(Append)["append"],
            P.Id("log-count")[_log.Count.ToString(CultureInfo.InvariantCulture)],
            Virtualize.Items(
                ctx => Div.Id("table-box").Style(Box).OnScroll(ctx.OnScroll)[
                    Table.Style("width:100%;border-collapse:collapse;")[Tbody[TableRows(ctx)]]
                ],
                _rows,
                ItemSize: 30,
                InitialClientHeight: 320)
        ];

    private async Task Go()
    {
        await _scroller.ScrollTo(Target);
        _landed = "landed:" + Target.ToString(CultureInfo.InvariantCulture);
    }

    private void Append() => _log.AddRange(Enumerable.Range(_log.Count, Added));

    private static IEnumerable<Component> MixedRows(VirtualizationContext<int> ctx)
    {
        yield return ctx.Above;
        foreach (var item in ctx.VisibleItems)
        {
            yield return Div.Key(item.Index).Style(Row).Data("row", Number(item.Index))[Lines("row", item.Index)];
        }

        yield return ctx.Below;
    }

    // The log's rows are not keyed: the morph refills them where they stand, and the view is held all the same.
    private static IEnumerable<Component> LogRows(VirtualizationContext<int> ctx)
    {
        yield return ctx.Above;
        foreach (var item in ctx.VisibleItems)
        {
            yield return Div.Style(Row).Data("row", Number(item.Index))[Lines("entry", item.Index)];
        }

        yield return ctx.Below;
    }

    private static IEnumerable<Component> TableRows(VirtualizationContext<int> ctx)
    {
        yield return ctx.AboveRow;
        foreach (var item in ctx.VisibleItems)
        {
            yield return Tr.Key(item.Index).Data("row", Number(item.Index))[
                Td.Style(Row)[Lines("cell", item.Index)]
            ];
        }

        yield return ctx.BelowRow;
    }

    private static IEnumerable<Component> Lines(string what, int row)
    {
        for (var line = 1; line <= Lines(row); line++)
        {
            yield return Div[what + " " + Number(row) + " · line " + Number(line)];
        }
    }

    private static string Number(int n) => n.ToString(CultureInfo.InvariantCulture);
}
