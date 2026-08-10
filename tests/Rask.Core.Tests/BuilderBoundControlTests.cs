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
            Input(() => Model.Name).Validate(NonEmpty).Id("name").Class("field"),
            Input(() => Model.Age).Id("age"),
            Textarea(() => Model.Name).Id("bio"),
            Select(() => Model.Name).Id("pick")[Option("Ada")["Ada"]]
        ];

    internal static IEnumerable<string> NonEmpty(string value) =>
        value.Length > 0 ? Array.Empty<string>() : new[] { "required" };
}

internal sealed partial class BoundFactoryProbe : Component
{
    internal readonly BoundForm Model = new() { Name = "Ada", Age = 36 };

    protected override Component? Render() =>
        Div()[
            Rask.Core.Components.Generated.Input(() => Model.Name, Validate: BoundBuilderProbe.NonEmpty,
                Id: "name", Class: "field"),
            Rask.Core.Components.Generated.Input(() => Model.Age, Id: "age"),
            Rask.Core.Components.Generated.Textarea(() => Model.Name, Id: "bio"),
            Rask.Core.Components.Generated.Select(() => Model.Name, Id: "pick")[Option("Ada")["Ada"]]
        ];
}

public class BuilderBoundControlTests
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
        var sync = Rask.Core.Components.Generated.Input<string>().Bind(() => model.Name);
        var async = Rask.Core.Components.Generated.Input<string>().Bind(() => model.Name);

        sync.Validate(BoundBuilderProbe.NonEmpty);
        async.ValidateAsync(CheckAsync);

        Assert.Same((Validate<string>)BoundBuilderProbe.NonEmpty, sync.Validate?.Fn);
        Assert.NotNull(async.ValidateAsync?.Fn);
        Assert.Null(async.Validate?.Fn);
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
        var control = Rask.Core.Components.Generated.Input<string>();
        Action<string> hook = probe.Note;

        control.AfterBind(hook);

        Assert.Same(hook, control.AfterBind?.Fn);
    }

    // The carrier is what lets the prop and the setter share a name; plain assignment must still work.
    [Fact]
    public void The_carrier_converts_from_the_plain_delegate()
    {
        Validate<string> rule = BoundBuilderProbe.NonEmpty;
        var control = Rask.Core.Components.Generated.Input<string>();
        control.Validate = rule;

        Assert.Same(rule, control.Validate?.Fn);
    }
}

internal sealed partial class BoundBuilderProbe
{
    internal void Note(string value) { }
}
