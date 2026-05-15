using Rask.Core.Components;
using Rask.Core.DataGrids;
using Rask.Core.Tests.Live;

#pragma warning disable RASK014

namespace Rask.Core.Tests.Components;

public class DataGridTests
{
    private record Row(int Id, string Name);

    [Fact]
    public void Render_NoChildren_EmitsEmpty() =>
        Assert.Equal(string.Empty, DataGrid<Row>(Source: new[] { new Row(1, "a") }).ToHtml());

    [Fact]
    public void Render_WithChildren_EmitsChildrenInOrder()
    {
        var html = DataGrid<Row>(Source: new[] { new Row(1, "a") })[
            Span()["before"],
            Span()["after"]
        ].ToHtml();

        Assert.Equal("<span>before</span><span>after</span>", html);
    }

    [Fact]
    public void EnterChildrenScope_PushesContext_AccessibleToDescendant()
    {
        DataGridContext<Row>? captured = null;
        var probe = new ScopeProbe(() => { captured = DataGridScope.CurrentAs<Row>(); });
        var view = new StubComponent(() => DataGrid<Row>(Source: new[] { new Row(1, "a") }, PageSize: 2)[probe]);

        view.RenderAsLiveRoot();

        Assert.NotNull(captured);
        Assert.Equal(2, captured!.PageSize);
        Assert.Single(captured.Source);
    }

    [Fact]
    public void EnterChildrenScope_NullSource_StillPushesEmptyContext()
    {
        DataGridContext<Row>? captured = null;
        var probe = new ScopeProbe(() => { captured = DataGridScope.CurrentAs<Row>(); });
        var view = new StubComponent(() => DataGrid<Row>(Source: null, PageSize: 0)[probe]);

        view.RenderAsLiveRoot();

        Assert.NotNull(captured);
        Assert.Empty(captured!.Source);
    }

    [Fact]
    public void ContextSurvivesReRender_WhenSameSourceReference()
    {
        var data = new[] { new Row(1, "a") };
        DataGridContext<Row>? first = null;
        DataGridContext<Row>? second = null;
        var phase = 0;
        var probe = new ScopeProbe(() =>
        {
            if (phase == 0) first = DataGridScope.CurrentAs<Row>();
            else second = DataGridScope.CurrentAs<Row>();
        });

        var view = new StubComponent(() => DataGrid<Row>(Source: data, PageSize: 1)[probe]);
        view.RenderAsLiveRoot();
        phase = 1;
        view.RenderAsLiveRoot();

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    private sealed class ScopeProbe : Component
    {
        private readonly Action _onRender;
        public ScopeProbe(Action onRender) => _onRender = onRender;

        protected internal override bool BypassRenderCache => true;

        protected override Component Render()
        {
            _onRender();
            return new Fragment();
        }
    }
}
