using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server;
using static Rask.Markup;

#pragma warning disable RASK019 // a benchmark page has nothing to put in <head> beyond its title

namespace Rask.Benchmarks;

/// <summary>
///     A big table's first render — thousands of rows of fourteen cells — as plain HTML and as a live first GET,
///     drawn with the kit's table parts and with bare <c>tr</c>/<c>td</c> carrying the same classes.
/// </summary>
/// <remarks>
///     A report rather than a BenchmarkDotNet class: one render of 5,000 rows takes seconds, and the question is
///     whether the cost per cell stays flat from 500 rows to 20,000. Usage: <c>big-table [rows…] [--get]</c>
///     (<c>--get</c>: the live GET alone).
/// </remarks>
internal static partial class BigTableReport
{
    private const int Cells = 14;

    internal static int Rows { get; private set; }

    internal static bool Kit { get; private set; }

    public static int Run(string[] args)
    {
        var getOnly = args.Contains("--get", StringComparer.Ordinal);
        var sizes = args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal))
            .Select(a => int.Parse(a, CultureInfo.InvariantCulture)).ToArray();
        if (sizes.Length == 0)
        {
            sizes = [500, 5000];
        }

        using var host = new Host();
        foreach (var kit in new[] { true, false })
        {
            foreach (var rows in sizes)
            {
                Kit = kit;
                Rows = rows;
                if (!getOnly)
                {
                    Measure(kit, rows, "a html", () => Table().ToHtml().Length);
                }

                Measure(kit, rows, "b get ", () => host.Get());
            }
        }

        return 0;
    }

    private static void Measure(bool kit, int rows, string arm, Func<int> run)
    {
        // A run past 10,000 rows is long enough to be its own warm-up, and slow enough before a fix to take minutes.
        var runs = rows > 10_000 ? 1 : 3;
        if (runs > 1)
        {
            run();
        }

        GC.Collect();
        var best = TimeSpan.MaxValue;
        long allocated = 0;
        var length = 0;
        for (var i = 0; i < runs; i++)
        {
            var before = GC.GetTotalAllocatedBytes(precise: true);
            var watch = Stopwatch.StartNew();
            length = run();
            watch.Stop();
            allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
            best = watch.Elapsed < best ? watch.Elapsed : best;
        }

        var cells = rows * Cells;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{(kit ? "kit  " : "plain")} {arm} {rows,6} rows  {best.TotalMilliseconds,9:F1} ms  {best.TotalMicroseconds / cells,7:F2} µs/cell  {allocated / cells,7} B/cell  {length / 1024,7} KB"));
    }

    internal static Component Table() => Kit ? KitTable() : PlainTable();

    private static Component KitTable() =>
        Ui.Table[Ui.TableRows[Enumerable.Range(0, Rows).Select(i => (Component)Ui.TableRow.Key(i)[Row(() => Ui.TableCell.Class("truncate").Title("x")["x"])])]];

    // Fourteen fixed columns, as a page writes them: positional siblings, not a keyed list.
    private static Component[] Row(Func<Component> cell)
    {
        var cells = new Component[Cells];
        for (var c = 0; c < Cells; c++)
        {
            cells[c] = cell();
        }

        return cells;
    }

    // The classes the kit writes on a cell today, read off one it rendered, so the arm differs only in what draws them.
    private static readonly string CellClass = KitCellClass();

    private static string KitCellClass()
    {
        var html = Ui.TableCell.Class("truncate")["x"].ToHtml();
        var start = html.IndexOf("class=\"", StringComparison.Ordinal) + "class=\"".Length;
        return System.Net.WebUtility.HtmlDecode(html[start..html.IndexOf('"', start)]);
    }

    private static Component PlainTable() =>
        Markup.Table[Tbody[Enumerable.Range(0, Rows).Select(i => (Component)Tr.Key(i)[Row(() => Td.Data("ui-cell", null).Class(CellClass).Title("x")["x"])])]];

    private sealed class Host : IDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        public Host()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddRouting();
            builder.Services.AddRask(configureServer: o => o.UnconnectedSessionGracePeriod = TimeSpan.FromMilliseconds(50));
            _app = builder.Build();
            _app.UseRouting();
            _app.UseWebSockets();
            _app.MapRask<BigTableApp>();
            _app.StartAsync().GetAwaiter().GetResult();
            _client = _app.GetTestServer().CreateClient();
        }

        public int Get()
        {
            using var response = _client.GetAsync(BigTablePage.Path).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult().Length;
        }

        public void Dispose()
        {
            _client.Dispose();
            _app.StopAsync().GetAwaiter().GetResult();
            _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}

public sealed partial class BigTableApp : Component
{
    protected override Component? HeadAssets => Title["big-table"];

    protected override Component? Render() => Router;
}

[Route(Path)]
public sealed partial class BigTablePage : Component
{
    public const string Path = "/benchmarks/big-table";

    protected override Component? Render() => BigTableReport.Table();
}
