using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("binding")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class BindingPage : Component
{
    protected override RenderResult Head => Title()["Two-way binding — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Two-way binding",
            "Inputs can be wired with plain Value + OnInput, or with a strongly-typed Bind expression that resolves the input name, type, and update timing for you."),
        H2(Class: "h4 mt-4 mb-3")["Manual — Value + OnInput"],
        CodeSample(
            EmbeddedSource.Read("BindingManualDemo.cs"),
            Notes:
            "The low-level path: wire Value and the event handler yourself. Works for any input type, but you parse and re-render manually.",
            Result: BindingManualDemo()),
        H2(Class: "h4 mt-5 mb-3")["Typed — Input<TProp>(Bind: ...)"],
        CodeSample(
            EmbeddedSource.Read("BindingTypedDemo.cs"),
            Notes:
            "Bind reads the expression — the property name becomes the input name, the property type picks the input type, and string fields update on every keystroke. One call replaces Value + OnInput + parsing.",
            Result: BindingTypedDemo()),
        H2(Class: "h4 mt-5 mb-3")["Typed — across primitive types"],
        CodeSample(
            EmbeddedSource.Read("BindingMultiDemo.cs"),
            Notes:
            "The same Bind helper picks the right input type from the property's CLR type and wires immediate (string) or change-deferred (everything else) update timing automatically.",
            Result: BindingMultiDemo()),
        H2(Class: "h4 mt-5 mb-3")["Typed — nullable properties"],
        CodeSample(
            EmbeddedSource.Read("BindingNullableDemo.cs"),
            Notes:
            "BindingHelpers.TrySetTyped routes empty input to null when the property is nullable — either Nullable<T> for value types or NRT-annotated for reference types (detected via NullabilityInfoContext). Non-nullable string still becomes \"\" when emptied; non-nullable value types clear to default(T) — see the next section.",
            Result: BindingNullableDemo()),
        H2(Class: "h4 mt-5 mb-3")["Typed — clearing a non-nullable value-type input"],
        CodeSample(
            EmbeddedSource.Read("BindingClearDefaultDemo.cs"),
            Notes:
            "Clearing a number/date/enum input on a non-nullable value type now snaps the model to default(T) instead of silently reverting. The nullable companion still routes empty → null, so the two paths stay distinguishable.",
            Result: BindingClearDefaultDemo()),
        H2(Class: "h4 mt-5 mb-3")["Typed — Textarea<TProp>(Bind: ...)"],
        CodeSample(
            EmbeddedSource.Read("BindingTextareaDemo.cs"),
            Notes:
            "Textareas always stream — Textarea.Bound wires OnInputAsync for every keystroke so the echo updates without blur or submit, no matter how long the text is.",
            Result: BindingTextareaDemo()),
        H2(Class: "h4 mt-5 mb-3")["Typed — AfterBind (sync)"],
        CodeSample(
            EmbeddedSource.Read("BindingAfterBindDemo.cs"),
            Notes:
            "AfterBind fires after TrySetTyped has written the new value to the model and before validators run. The dependent dropdown rebinds in the same render — no extra round-trip.",
            Result: BindingAfterBindDemo()),
        H2(Class: "h4 mt-5 mb-3")["Typed — AfterBindAsync (async)"],
        CodeSample(
            EmbeddedSource.Read("BindingAfterBindAsyncDemo.cs"),
            Notes:
            "Rask re-renders at every await suspension inside an async handler, so setting _loading before the await surfaces the \"loading…\" UI on its own — no manual StateHasChanged() is required. AfterBindAsync is still awaited before the post-handler render, so validators see the new dependent state. Note the empty-value placeholder option: it matches the initial model so the dropdown doesn't falsely show the first track, and so picking any track (including the first) fires a real change.",
            Result: BindingAfterBindAsyncDemo())
    ];
}
