using Rask.Core.Components;
using Rask.Core.DataGrids;
using Rask.Core.Tests.Live;

#pragma warning disable RASK014

namespace Rask.Core.Tests.Components;

public class DataGridRowsTests
{
    private record Row(int Id, string Name);

    [Fact]
    public void Render_NoAmbientContext_EmitsEmpty()
    {
        var html = DataGridRows<Row>(Row: r => Span()[r.Name]).ToHtml();
        Assert.Equal(string.Empty, html);
    }

    [Fact]
    public void Render_InsideDataGrid_EmitsOneFragmentPerVisibleRow()
    {
        var rows = new[] { new Row(1, "Ada"), new Row(2, "Bob") };
        var view = new StubComponent(() => DataGrid<Row>(Source: rows)[
            DataGridRows<Row>(Row: r => Span()[r.Name])
        ]);

        var html = view.RenderAsLiveRoot();

        Assert.Equal("<span>Ada</span><span>Bob</span>", html);
    }

    [Fact]
    public void Render_RespectsPageSize()
    {
        var rows = new[] { new Row(1, "Ada"), new Row(2, "Bob"), new Row(3, "Cy") };
        var view = new StubComponent(() => DataGrid<Row>(Source: rows, PageSize: 2)[
            DataGridRows<Row>(Row: r => Span()[r.Name])
        ]);

        var html = view.RenderAsLiveRoot();

        Assert.Equal("<span>Ada</span><span>Bob</span>", html);
    }
}
