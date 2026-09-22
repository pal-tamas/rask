using Rask.Core.Forms;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public partial class LiveRenderContextEditContextTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_same_model_gets_the_cached_edit_context()
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
    public void Different_models_get_different_edit_contexts()
    {
        var view = new StubComponent(Span);
        using var ctx = LiveRenderContext.Begin(view);

        var ec1 = ctx.GetOrCreateEditContext(new Model());
        var ec2 = ctx.GetOrCreateEditContext(new Model());

        Assert.NotSame(ec1, ec2);
    }

    [Fact]
    public void The_edit_context_factory_is_used_on_the_first_call_only()
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
    public void Without_services_the_factory_is_invoked_with_a_null_provider()
    {
        // HTML tag wrappers don't need DI — their generated factories use the closure
        // form `__sp => new T() { ... }` which ignores the services parameter. The context
        // therefore passes null through cleanly so tag factories work in tests that
        // construct a LiveRenderContext without an IServiceProvider.
        var view = new StubComponent(Span);
        using var ctx = LiveRenderContext.Begin(view);

        var span = ctx.GetOrCreate<Span>(_ => Span);

        Assert.NotNull(span);
    }

    private sealed class Model
    {
        public string Name { get; set; } = "";
    }
}
