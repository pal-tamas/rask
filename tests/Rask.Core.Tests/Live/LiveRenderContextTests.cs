using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public partial class LiveRenderContextTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_current_context_is_null_outside_of_begin() => Assert.Null(LiveRenderContext.Current);

    [Fact]
    public void The_current_context_is_set_inside_begin_and_dispose_restores_the_previous()
    {
        var view = new StubComponent(Span);
        Assert.Null(LiveRenderContext.Current);
        using (LiveRenderContext.Begin(view))
        {
            Assert.NotNull(LiveRenderContext.Current);
        }

        Assert.Null(LiveRenderContext.Current);
    }

    [Fact]
    public void Registering_handlers_yields_sequential_ids()
    {
        var view = new StubComponent(Span);
        using var ctx = LiveRenderContext.Begin(view);
        var a = () => { };
        var b = () => { };
        var c = () => { };

        Assert.Equal("h0", ctx.RegisterHandler(a));
        Assert.Equal("h1", ctx.RegisterHandler(b));
        Assert.Equal("h2", ctx.RegisterHandler(c));
    }
}
