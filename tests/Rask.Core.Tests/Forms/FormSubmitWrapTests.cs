#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// Does the builder surface's untyped `OnValidSubmit(Delegate?)` setter have to AutoCallback-wrap what it
// is handed, or is the wrap redundant for Form?
//
// The two surfaces disagree today. Form folds its submit handler into one untyped `Delegate?` property,
// and the GENERIC factory — the overload every real call site uses — wraps on the way in; the
// NON-generic one, and the setter generated from the property, assign it raw. A method group reaches
// that setter through its natural type, so `Form.Model(m).OnValidSubmit(SaveAsync)` compiles, looks
// right, and skips the wrap.
//
// Whether that matters is not answerable by reasoning about it. What the wrap adds is
// `StateHasChanged()` on the component that OWNS the handler, and a form submit already arrives through
// a DOM handler whose owner resolution re-renders — but the owner it resolves is whoever was rendering
// when the handler was registered, which is not necessarily whoever owns the method. So the shape to
// test is the one where those two differ: the Form is rendered by a CHILD, and the handler belongs to
// an ANCESTOR whose own markup is what changes.
internal sealed class WrapModel
{
    public string Name { get; set; } = "Ada";
}

// The handler's owner, and the component whose markup moves. It renders the status itself and delegates
// the form to a child, so nothing about the submit is registered while THIS component is rendering.
internal sealed partial class WrapAncestor : Component
{
    internal readonly WrapModel Model = new();

    internal string Status = "idle";

    internal bool UseBuilderSurface;

    internal Task SaveAsync(WrapModel m)
    {
        Status = "saved";
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
        Div[
            Span[Status],
            WrapFormChild.Owner(this)
        ];
}

// Renders the form and owns nothing. The submit handler is registered during THIS component's render,
// so DOM handler-owner resolution marks this one dirty — never the ancestor.
internal sealed partial class WrapFormChild : Component
{
    public WrapAncestor? Owner { get; set; }

    protected override Component? Render()
    {
        var owner = Owner!;
        return owner.UseBuilderSurface
            // The naive migration of the line below: the handler set through the chain, with no wrapping
            // anywhere. `SaveAsync` returns Task, so it is the async handler on both arms — what differs
            // is which surface registered it.
            ? Form.Model(owner.Model).OnValidSubmitAsync(owner.SaveAsync)[Input.Bind(() => owner.Model.Name)]
            : Form.Model(owner.Model).OnValidSubmitAsync(owner.SaveAsync)[
                Input.Bind(() => owner.Model.Name)
            ];
    }
}

public class FormSubmitWrapTests
{
    private static async Task<string> SubmitAsync(WrapAncestor ancestor)
    {
        var page = RaskTest.Render(ancestor);
        Assert.Contains("<span>idle</span>", page.Html, StringComparison.Ordinal);

        await page.On("form").SubmitAsync("{\"form\":{\"Name\":\"Ada\"}}");
        return page.Html;
    }

    // Baseline: the surface that ships today. If this ever stops showing "saved", the comparison below
    // is measuring the wrong thing.
    [Fact]
    public async Task The_typed_factory_repaints_the_component_that_owns_the_handler()
    {
        var html = await SubmitAsync(new WrapAncestor());

        Assert.Contains("<span>saved</span>", html, StringComparison.Ordinal);
    }

    // The question. Same tree, same handler, same submit — only the surface differs.
    [Fact]
    public async Task The_builder_setter_repaints_it_too()
    {
        var html = await SubmitAsync(new WrapAncestor { UseBuilderSurface = true });

        Assert.Contains("<span>saved</span>", html, StringComparison.Ordinal);
    }
}
