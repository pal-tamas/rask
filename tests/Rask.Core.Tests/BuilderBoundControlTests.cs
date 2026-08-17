// rask-rewrite: keep the factory — this file holds BOTH surfaces on purpose and asserts they agree.
// Converting the factory half would leave a test comparing a chain to itself: still green, proving
// nothing. tools/RaskBuilderRewrite skips any file carrying this marker.

using Rask.Core.Components;
using Rask.Core.Forms;
using static Rask.Core.Tests.Generated;

namespace Rask.Core.Tests;

// PROTOTYPE — A4: the bound form controls on the builder surface.
//
// A property cannot be generic, so `Input<T>` / `Select<T>` / `Textarea<T>` get a static METHOD entry
// whose single argument (the bind expression) is what infers T. The generated factory needed THREE
// overloads per control for exactly one reason — `Validate` had to be a required, correctly-typed
// parameter, and sync `Validate<T>` cannot share a parameter with async `ValidateAsync<T>` without
// losing inference. On this surface that fan-out collapses: one entry, and the validator is a setter.
public sealed class BoundForm
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

internal sealed partial class BoundBuilderProbe : Component
{
    internal readonly BoundForm Model = new() { Name = "Ada", Age = 36 };

    protected override Component? Render() =>
        Div[
            Input.Bind(() => Model.Name).Validate(NonEmpty).Id("name").Class("field"),
            Input.Bind(() => Model.Age).Id("age"),
            Textarea.Bind(() => Model.Name).Id("bio"),
            Select.Bind(() => Model.Name).Id("pick")[Option("Ada")["Ada"]]
        ];

    internal static IEnumerable<string> NonEmpty(string value) =>
        value.Length > 0 ? Array.Empty<string>() : new[] { "required" };
}

internal sealed partial class BoundFactoryProbe : Component
{
    internal readonly BoundForm Model = new() { Name = "Ada", Age = 36 };

    protected override Component? Render() =>
        Div()[
            Rask.Core.Components.Generated.Input(() => Model.Name, Validate: global::Rask.Core.Tests.BoundBuilderProbe.NonEmpty,
                Id: "name", Class: "field"),
            Rask.Core.Components.Generated.Input(() => Model.Age, Id: "age"),
            Rask.Core.Components.Generated.Textarea(() => Model.Name, Id: "bio"),
            Rask.Core.Components.Generated.Select(() => Model.Name, Id: "pick")[Option("Ada")["Ada"]]
        ];
}

public partial class BuilderBoundControlTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_bound_entry_renders_identically_to_the_bound_factory() =>
        Assert.Equal(BoundFactoryProbe().ToHtml(), BoundBuilderProbe().ToHtml());

    // The point of the entry: T comes from the bind expression, so `int` picks type="number" while the
    // string field stays text — without the caller ever writing a type argument.
    [Fact]
    public void The_entry_infers_the_value_type_from_the_bind_expression()
    {
        var html = BoundBuilderProbe().ToHtml();
        Assert.Contains("<input id=\"age\" type=\"number\" name=\"Age\" value=\"36\"", html, StringComparison.Ordinal);
        Assert.Contains("<input id=\"name\" class=\"field\" type=\"text\" name=\"Name\" value=\"Ada\"", html,
            StringComparison.Ordinal);
    }

    // Both validator shapes are ordinary setters now — the none/sync/async overload fan-out is gone.
    [Fact]
    public void Both_validator_shapes_are_setters()
    {
        var model = new BoundForm();
        // `Bind` is the chain's opening, not a setter on a built control — which is what makes bound and
        // controlled mutually exclusive. So the chain starts here rather than at the factory.
        var sync = Input.Bind(() => model.Name).Validate(global::Rask.Core.Tests.BoundBuilderProbe.NonEmpty).Value;
        var async = Input.Bind(() => model.Name).ValidateAsync(CheckAsync).Value;

        Assert.Same((Validate<string>)global::Rask.Core.Tests.BoundBuilderProbe.NonEmpty, sync.Validate);
        Assert.NotNull(async.ValidateAsync);
        Assert.Null(async.Validate);
        return;

        static ValueTask<IEnumerable<string>> CheckAsync(string value, CancellationToken ct) =>
            new(Array.Empty<string>());
    }

    // A validator is not an event callback and AfterBind is a post-bind hook, so neither may be
    // AutoCallback-wrapped — the setter must hand the delegate through untouched.
    [Fact]
    public void The_bound_setters_never_auto_wrap()
    {
        var probe = BoundBuilderProbe();
        Action<string> hook = probe.Note;

        // Through Bind, not Of: AfterBind is a BOUND step, so it exists only on a chain that opened in
        // bound mode. `Input.Of<string>()` is controlled — the parent owns the value — and asking it for
        // a post-bind hook no longer compiles, which is the point.
        var control = Input.Bind(() => probe.Model.Name).AfterBind(hook).Value;

        Assert.Same(hook, control.AfterBind);
    }

    // Plain assignment must still work.
    [Fact]
    public void A_bound_member_takes_a_plain_assignment()
    {
        Validate<string> rule = global::Rask.Core.Tests.BoundBuilderProbe.NonEmpty;
        var control = Rask.Core.Components.Generated.Input<string>();
        control.Validate = rule;

        Assert.Same(rule, control.Validate);
    }
}

internal sealed partial class BoundBuilderProbe
{
    internal void Note(string value) { }
}
