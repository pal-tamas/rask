using Rask.Core.Components;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

public class LiveRenderContextEditContextTests
{
    [Fact]
    public void GetOrCreateEditContext_SameModel_ReturnsCachedInstance()
    {
        var view = new StubComponent(new Span(null));
        var model = new Model();
        using var ctx = LiveRenderContext.Begin(view);

        var first = ctx.GetOrCreateEditContext(model);
        var second = ctx.GetOrCreateEditContext(model);

        Assert.Same(first, second);
        Assert.Same(model, first.Model);
    }

    [Fact]
    public void GetOrCreateEditContext_DifferentModels_ReturnsDifferentInstances()
    {
        var view = new StubComponent(new Span(null));
        using var ctx = LiveRenderContext.Begin(view);

        var ec1 = ctx.GetOrCreateEditContext(new Model());
        var ec2 = ctx.GetOrCreateEditContext(new Model());

        Assert.NotSame(ec1, ec2);
    }

    [Fact]
    public void GetOrCreateEditContext_FactoryUsed_OnFirstCallOnly()
    {
        var view = new StubComponent(new Span(null));
        var model = new Model();
        using var ctx = LiveRenderContext.Begin(view);
        var calls = 0;

        EditContext Factory()
        {
            calls++;
            return new EditContext(model);
        }

        var a = ctx.GetOrCreateEditContext(model, Factory);
        var b = ctx.GetOrCreateEditContext(model, Factory);

        Assert.Same(a, b);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void GetOrCreate_WithoutServices_ThrowsForNewComponent()
    {
        var view = new StubComponent(new Span(null));
        using var ctx = LiveRenderContext.Begin(view);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ctx.GetOrCreate<Span>(_ => new Span(null)));

        Assert.Contains("IServiceProvider", ex.Message);
    }

    private sealed class Model
    {
        public string Name { get; set; } = "";
    }
}
