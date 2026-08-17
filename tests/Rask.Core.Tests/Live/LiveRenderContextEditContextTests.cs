using Rask.Core.Forms;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public partial class LiveRenderContextEditContextTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void GetOrCreateEditContext_SameModel_ReturnsCachedInstance()
    {
        var view = new StubComponent(Span);
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
        var view = new StubComponent(Span);
        using var ctx = LiveRenderContext.Begin(view);

        var ec1 = ctx.GetOrCreateEditContext(new Model());
        var ec2 = ctx.GetOrCreateEditContext(new Model());

        Assert.NotSame(ec1, ec2);
    }

    [Fact]
    public void GetOrCreateEditContext_FactoryUsed_OnFirstCallOnly()
    {
        var view = new StubComponent(Span);
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
    public void GetOrCreate_WithoutServices_InvokesFactoryWithNullProvider()
    {
        // HTML tag wrappers don't need DI — their generated factories use the closure
        // form `__sp => new T() { ... }` which ignores the services parameter. The context
        // therefore passes null through cleanly so tag factories work in tests that
        // construct a LiveRenderContext without an IServiceProvider.
        var view = new StubComponent(Span);
        using var ctx = LiveRenderContext.Begin(view);

        var span = ctx.GetOrCreate<global::Rask.Html.Components.Span>(_ => Span);

        Assert.NotNull(span);
    }

    private sealed class Model
    {
        public string Name { get; set; } = "";
    }
}
