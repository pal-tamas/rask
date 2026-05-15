using Rask.Core.Components;
using Rask.Core.Tests.Live;

#pragma warning disable RASK014

namespace Rask.Core.Tests.Components;

public class DataGridPagerTests
{
    private record Row(int Id);

    [Fact]
    public void Render_NoAmbientContext_EmitsEmpty() =>
        Assert.Equal(string.Empty, DataGridPager().ToHtml());

    [Fact]
    public void Render_DefaultTemplate_ShowsPageOf()
    {
        var rows = Enumerable.Range(0, 5).Select(i => new Row(i)).ToArray();
        var view = new StubComponent(() => DataGrid<Row>(Source: rows, PageSize: 2)[DataGridPager()]);

        var html = view.RenderAsLiveRoot();

        Assert.Contains("<nav>", html);
        Assert.Contains("Page 1 of 3", html);
        Assert.Contains(">Prev</button>", html);
        Assert.Contains(">Next</button>", html);
        Assert.Contains("disabled", html);  // Prev disabled on page 0
    }

    [Fact]
    public void Render_CustomTemplate_ReceivesState()
    {
        var rows = Enumerable.Range(0, 7).Select(i => new Row(i)).ToArray();
        var view = new StubComponent(() => DataGrid<Row>(Source: rows, PageSize: 3)[
            DataGridPager(Template: state =>
                Span()[$"{state.CurrentPage + 1}/{state.PageCount}"])
        ]);

        var html = view.RenderAsLiveRoot();

        Assert.Equal("<span>1/3</span>", html);
    }
}
