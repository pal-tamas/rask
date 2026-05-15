using System.Linq.Expressions;
using Rask.Core.Components;
using Rask.Core.Tests.Live;

#pragma warning disable RASK014

namespace Rask.Core.Tests.Components;

public class DataGridSortButtonTests
{
    private record Row(int Id, string Name);

    [Fact]
    public void Render_NoAmbientContext_EmitsButtonWithoutHandler()
    {
        var html = DataGridSortButton<Row>(SortBy: r => r.Name)["Name"].ToHtml();
        Assert.Equal("<button type=\"button\">Name</button>", html);
    }

    [Fact]
    public void Render_InsideLiveDataGrid_EmitsHandlerIdOnButton()
    {
        var rows = new[] { new Row(1, "Ada") };
        var view = new StubComponent(() => DataGrid<Row>(Source: rows)[
            DataGridSortButton<Row>(SortBy: r => r.Name)["Name"]
        ]);

        var html = view.RenderAsLiveRoot();

        Assert.Contains("data-rask-on-click=\"h0\"", html);
        Assert.Contains(">Name</button>", html);
    }

    [Fact]
    public void ExplicitKey_OverridesExpressionDerivedKey()
    {
        var rows = new[] { new Row(1, "Ada") };
        var button = DataGridSortButton<Row>(SortBy: r => r.Name, Key: "n");
        // No throw expected; the button compiles selector + uses provided key
        var view = new StubComponent(() => DataGrid<Row>(Source: rows)[button]);
        var html = view.RenderAsLiveRoot();
        Assert.Contains("<button", html);
    }

    [Fact]
    public void MethodCallExpression_WithoutExplicitKey_Throws()
    {
        var rows = new[] { new Row(1, "Ada") };
        var view = new StubComponent(() => DataGrid<Row>(Source: rows)[
            DataGridSortButton<Row>(SortBy: r => r.Name.ToUpperInvariant())["X"]
        ]);

        Assert.Throws<ArgumentException>(() => view.RenderAsLiveRoot());
    }
}
