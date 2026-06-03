using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public class LiveRenderContextTests
{
    [Fact]
    public void Current_OutsideOfBegin_IsNull() => Assert.Null(LiveRenderContext.Current);

    [Fact]
    public void Current_InsideBegin_IsNonNull_AndDispose_RestoresPrevious()
    {
        var view = new StubComponent(Span());
        Assert.Null(LiveRenderContext.Current);
        using (LiveRenderContext.Begin(view))
        {
            Assert.NotNull(LiveRenderContext.Current);
        }

        Assert.Null(LiveRenderContext.Current);
    }

    [Fact]
    public void RegisterHandler_YieldsSequentialIds()
    {
        var view = new StubComponent(Span());
        using var ctx = LiveRenderContext.Begin(view);
        var a = () => { };
        var b = () => { };
        var c = () => { };
        Assert.Equal("h0", ctx.RegisterHandler(a));
        Assert.Equal("h1", ctx.RegisterHandler(b));
        Assert.Equal("h2", ctx.RegisterHandler(c));
    }
}
