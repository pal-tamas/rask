#pragma warning disable RASK014 // test-defined components constructed directly

namespace Rask.Testing.Tests;

// The factory overload exists because Render(Component) renders one fixed instance: a tree built once by
// the caller can never reflect state that changes afterwards. These pin that distinction.
public partial class RaskTestFactoryTests : global::Rask.Core.RaskMarkup
{
    private sealed class Model
    {
        public string Name { get; set; } = "Ada";
    }

    [Fact]
    public void Rendering_a_factory_reruns_it_so_a_rerender_sees_changed_state()
    {
        var model = new Model();
        var page = Test.Render(() => Div[$"Name: {model.Name}"]);

        Assert.Contains("Name: Ada", page.Html);

        model.Name = "Grace";
        page.Render();

        Assert.Contains("Name: Grace", page.Html);
    }

    [Fact]
    public void Rendering_a_component_keeps_the_tree_it_was_given_even_after_state_changes()
    {
        // The contrast that justifies the factory overload: the tree here is built once, at the call site,
        // so re-rendering replays the same baked children.
        var model = new Model();
        var page = Test.Render(Div[$"Name: {model.Name}"]);

        model.Name = "Grace";
        page.Render();

        Assert.Contains("Name: Ada", page.Html);
        Assert.DoesNotContain("Name: Grace", page.Html);
    }

    private sealed partial class Greeting : Component
    {
        public string Name { get; set; } = "";

        protected override Component? Render() => Span[$"Hi {Name}"];
    }

    [Fact]
    public void A_rendered_factory_passes_changed_props_to_a_child_component()
    {
        var model = new Model();
        var page = Test.Render(() => new Greeting { Name = model.Name });

        Assert.Contains("Hi Ada", page.Html);

        model.Name = "Grace";
        page.Render();

        Assert.Contains("Hi Grace", page.Html);
    }

    private sealed class Toggle : Component
    {
        private bool _on;

        protected override Component? Render() =>
            Button.Type("button").OnClick(() => _on = !_on)[_on ? "on" : "off"];
    }

    [Fact]
    public async Task A_rendered_factory_dispatches_handlers_and_rerenders_through_the_factory()
    {
        var page = Test.Render(() => new Toggle());

        // A fresh Toggle per render means the handler must still be wired on every frame.
        Assert.Contains("off", page.Html);
        Assert.NotNull(page.HandlerId("click"));

        await page.ClickAsync();

        Assert.NotNull(page.HandlerId("click"));
    }

    // Unmount is deliberately not asserted here: Unmount fires only for a child registered through its
    // generated factory, and this consumer-shaped project has no generator. The markup contract is what a
    // null factory result guarantees on its own.
    [Fact]
    public void A_factory_returning_null_renders_nothing()
    {
        var mounted = true;
        var page = Test.Render(() => mounted ? Span["here"] : null);

        Assert.Contains("here", page.Html);

        mounted = false;
        page.Render();

        Assert.DoesNotContain("here", page.Html);
    }

    [Fact]
    public void Rendering_a_null_factory_throws() =>
        Assert.Throws<ArgumentNullException>(() => Test.Render((Func<Component?>)null!));

    // A form control's chain is a Build<T, TMode>, not a Build<T>, so it needs its own Render overload —
    // inference runs before any user-defined conversion, so the chain cannot reach the Component-typed
    // parameter by itself (CS0315). Both modes, because they are two different constructed types and one
    // overload covering only one of them is the failure this pins.
    [Fact]
    public void Render_takes_a_form_control_chain_in_either_mode()
    {
        var model = new Model();

        Assert.Contains("value=\"Ada\"", Test.Render(Input.Bind(() => model.Name)).Html,
            StringComparison.Ordinal);
        Assert.Contains("value=\"Grace\"", Test.Render(Input.Value("Grace")).Html, StringComparison.Ordinal);
    }
}
