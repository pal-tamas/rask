#pragma warning disable RASK014 // test-defined components constructed directly

namespace Rask.Testing.Tests;

// The factory overload exists because Render(Component) renders one fixed instance: a tree built once by
// the caller can never reflect state that changes afterwards. These pin that distinction.
public class RaskTestFactoryTests
{
    private sealed class Model
    {
        public string Name { get; set; } = "Ada";
    }

    [Fact]
    public void RenderFactory_ReRunsTheFactory_SoARerenderSeesChangedState()
    {
        var model = new Model();
        var page = RaskTest.Render(() => Div()[$"Name: {model.Name}"]);
        Assert.Contains("Name: Ada", page.Html);

        model.Name = "Grace";
        page.Render();

        Assert.Contains("Name: Grace", page.Html);
    }

    [Fact]
    public void RenderComponent_KeepsTheTreeItWasGiven_EvenAfterStateChanges()
    {
        // The contrast that justifies the factory overload: the tree here is built once, at the call site,
        // so re-rendering replays the same baked children.
        var model = new Model();
        var page = RaskTest.Render(Div()[$"Name: {model.Name}"]);

        model.Name = "Grace";
        page.Render();

        Assert.Contains("Name: Ada", page.Html);
        Assert.DoesNotContain("Name: Grace", page.Html);
    }

    private sealed partial class Greeting : Component
    {
        public string Name { get; set; } = "";

        protected override Component? Render() => Span()[$"Hi {Name}"];
    }

    [Fact]
    public void RenderFactory_PassesChangedPropsToAChildComponent()
    {
        var model = new Model();
        var page = RaskTest.Render(() => new Greeting { Name = model.Name });
        Assert.Contains("Hi Ada", page.Html);

        model.Name = "Grace";
        page.Render();

        Assert.Contains("Hi Grace", page.Html);
    }

    private sealed class Toggle : Component
    {
        private bool _on;

        protected override Component? Render() =>
            Button(Type: "button", OnClick: () => _on = !_on)[_on ? "on" : "off"];
    }

    [Fact]
    public async Task RenderFactory_DispatchesHandlersAndReRendersThroughTheFactory()
    {
        var page = RaskTest.Render(() => new Toggle());

        // A fresh Toggle per render means the handler must still be wired on every frame.
        Assert.Contains("off", page.Html);
        Assert.NotNull(page.HandlerId("click"));
        await page.ClickAsync();
        Assert.NotNull(page.HandlerId("click"));
    }

    // Unmount is deliberately not asserted here: OnUnmount fires only for a child registered through its
    // generated factory, and this consumer-shaped project has no generator. The markup contract is what a
    // null factory result guarantees on its own.
    [Fact]
    public void RenderFactory_ReturningNull_RendersNothing()
    {
        var mounted = true;
        var page = RaskTest.Render(() => mounted ? Span()["here"] : null);
        Assert.Contains("here", page.Html);

        mounted = false;
        page.Render();

        Assert.DoesNotContain("here", page.Html);
    }

    [Fact]
    public void RenderFactory_NullFactory_Throws() =>
        Assert.Throws<ArgumentNullException>(() => RaskTest.Render((Func<Component?>)null!));
}
