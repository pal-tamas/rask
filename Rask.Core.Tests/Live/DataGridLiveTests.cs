using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core.Components;

#pragma warning disable RASK014

namespace Rask.Core.Tests.Live;

public class DataGridLiveTests
{
    private record Row(int Id, string Name, decimal Total);

    private static readonly Row[] FourRows =
    {
        new(1, "Bob", 30m),
        new(2, "Ada", 10m),
        new(3, "Ada", 25m),
        new(4, "Cy",  20m),
    };

    [Fact]
    public async Task SortButton_Click_ReordersRows_AscendingOnFirstClick()
    {
        var view = new StubComponent(() => DataGrid<Row>(Source: FourRows)[
            DataGridRows<Row>(Row: r => Span()[r.Name]),
            DataGridSortButton<Row>(SortBy: r => r.Name)["Name"]
        ]);

        var first = view.RenderAsLiveRoot();
        Assert.True(IndexOfRow(first, "Bob") < IndexOfRow(first, "Ada"));

        await view.TryInvokeHandlerAsync("h0", JsonDocument.Parse("{\"shiftKey\":false}").RootElement);
        var second = view.RenderAsLiveRoot();

        Assert.True(IndexOfRow(second, "Ada") < IndexOfRow(second, "Bob"));
        Assert.True(IndexOfRow(second, "Bob") < IndexOfRow(second, "Cy"));
    }

    [Fact]
    public async Task SortButton_ShiftClick_AppendsSecondarySort()
    {
        var view = new StubComponent(() => DataGrid<Row>(Source: FourRows)[
            DataGridRows<Row>(Row: r => Span()[$"{r.Name}-{r.Total}"]),
            DataGridSortButton<Row>(SortBy: r => r.Name)["Name"],
            DataGridSortButton<Row>(SortBy: r => r.Total)["Total"]
        ]);
        view.RenderAsLiveRoot();

        await view.TryInvokeHandlerAsync("h0", JsonDocument.Parse("{\"shiftKey\":false}").RootElement);
        await view.TryInvokeHandlerAsync("h1", JsonDocument.Parse("{\"shiftKey\":true}").RootElement);
        var html = view.RenderAsLiveRoot();

        // After Name asc + Total asc (shift): Ada-10, Ada-25, Bob-30, Cy-20
        var labels = ExtractRowLabels(html);
        Assert.Equal(new[] { "Ada-10", "Ada-25", "Bob-30", "Cy-20" }, labels);
    }

    [Fact]
    public async Task Pager_NextClick_AdvancesPage()
    {
        var rows = Enumerable.Range(1, 5).Select(i => new Row(i, $"R{i}", i)).ToArray();
        var view = new StubComponent(() => DataGrid<Row>(Source: rows, PageSize: 2)[
            DataGridRows<Row>(Row: r => Span()[r.Name]),
            DataGridPager()
        ]);

        var first = view.RenderAsLiveRoot();
        Assert.Contains("R1", first);
        Assert.Contains("R2", first);
        Assert.DoesNotContain("R3", first);
        Assert.Contains("Page 1 of 3", first);

        // Pager renders Prev (h0) + Next (h1). Prev is disabled on page 0; click Next.
        await view.TryInvokeHandlerAsync("h1", JsonDocument.Parse("{}").RootElement);
        var second = view.RenderAsLiveRoot();

        Assert.Contains("R3", second);
        Assert.Contains("R4", second);
        Assert.DoesNotContain("R1", second);
        Assert.Contains("Page 2 of 3", second);
    }

    [Fact]
    public async Task SortButton_ThreeStateCycle_SecondClickDesc_ThirdClickClears()
    {
        var view = new StubComponent(() => DataGrid<Row>(Source: FourRows)[
            DataGridRows<Row>(Row: r => Span()[r.Name]),
            DataGridSortButton<Row>(SortBy: r => r.Name)["Name"]
        ]);

        view.RenderAsLiveRoot();
        var payload = JsonDocument.Parse("{\"shiftKey\":false}").RootElement;

        await view.TryInvokeHandlerAsync("h0", payload);
        var asc = view.RenderAsLiveRoot();
        Assert.True(IndexOfRow(asc, "Ada") < IndexOfRow(asc, "Bob"));

        await view.TryInvokeHandlerAsync("h0", payload);
        var desc = view.RenderAsLiveRoot();
        Assert.True(IndexOfRow(desc, "Cy") < IndexOfRow(desc, "Bob"));
        Assert.True(IndexOfRow(desc, "Bob") < IndexOfRow(desc, "Ada"));

        await view.TryInvokeHandlerAsync("h0", payload);
        var cleared = view.RenderAsLiveRoot();
        // Unsorted — original order: Bob, Ada, Ada, Cy
        Assert.True(IndexOfRow(cleared, "Bob") < IndexOfRow(cleared, "Cy"));
        Assert.True(IndexOfRow(cleared, "Bob") < IndexOfRow(cleared, "Ada"));
    }

    private static int IndexOfRow(string html, string label) =>
        html.IndexOf(">" + label + "<", StringComparison.Ordinal);

    private static IReadOnlyList<string> ExtractRowLabels(string html)
    {
        var matches = Regex.Matches(html, @"<span>([^<]+)</span>");
        return matches.Select(m => m.Groups[1].Value)
            .Where(s => s.Contains('-'))
            .ToArray();
    }
}
