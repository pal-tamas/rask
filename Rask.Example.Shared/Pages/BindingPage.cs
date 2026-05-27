using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

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
                """
                Input(Type: "text",
                      Value: _typed,
                      OnInput: v => _typed = v)
                P()[$"Echo: {_typed}"]
                """,
                Notes:
                "The low-level path: wire Value and the event handler yourself. Works for any input type, but you parse and re-render manually.",
                Result: BindingManualDemo()),
            H2(Class: "h4 mt-5 mb-3")["Typed — Input<TProp>(Bind: ...)"],
            CodeSample(
                """
                Input(Bind: () => _model.Name,
                      Placeholder: "Your name")
                P()[$"Hello, {_model.Name}!"]
                """,
                Notes:
                "Bind reads the expression — the property name becomes the input name, the property type picks the input type, and string fields update on every keystroke. One call replaces Value + OnInput + parsing.",
                Result: BindingTypedDemo()),
            H2(Class: "h4 mt-5 mb-3")["Typed — across primitive types"],
            CodeSample(
                """
                Input(Bind: () => _model.Subscribe)   // bool   → checkbox
                Input(Bind: () => _model.Age)         // int    → number
                Input(Bind: () => _model.StartDate)   // DateOnly → date
                Select(Bind: () => _model.Favorite)[
                    Option("Red")["Red"],
                    Option("Green")["Green"],
                    Option("Blue")["Blue"]
                ]
                """,
                Notes:
                "The same Bind helper picks the right input type from the property's CLR type and wires immediate (string) or change-deferred (everything else) update timing automatically.",
                Result: BindingMultiDemo()),
            H2(Class: "h4 mt-5 mb-3")["Typed — nullable properties"],
            CodeSample(
                """
                Input(Bind: () => _model.OptionalAge)   // int?    → empty → null
                Input(Bind: () => _model.StartDate)     // DateOnly? → empty → null
                Select(Bind: () => _model.Favorite)[    // Color?
                    Option("")["— none —"],
                    Option("Red")["Red"], ...
                ]
                Input(Bind: () => _model.Nickname)      // string? (NRT) → empty → null
                """,
                Notes:
                "BindingHelpers.TrySetTyped routes empty input to null when the property is nullable — either Nullable<T> for value types or NRT-annotated for reference types (detected via NullabilityInfoContext). Non-nullable string still becomes \"\" when emptied; non-nullable value types clear to default(T) — see the next section.",
                Result: BindingNullableDemo()),
            H2(Class: "h4 mt-5 mb-3")["Typed — clearing a non-nullable value-type input"],
            CodeSample(
                """
                Input(Bind: () => _model.Age)         // int      → clear → 0
                Input(Bind: () => _model.OptionalAge) // int?     → clear → null
                """,
                Notes:
                "Clearing a number/date/enum input on a non-nullable value type now snaps the model to default(T) instead of silently reverting. The nullable companion still routes empty → null, so the two paths stay distinguishable.",
                Result: BindingClearDefaultDemo()),
            H2(Class: "h4 mt-5 mb-3")["Typed — Textarea<TProp>(Bind: ...)"],
            CodeSample(
                """
                Textarea(Bind: () => _model.Notes,
                         Rows: 3,
                         Placeholder: "Jot something down…")
                Pre()[$"Notes = \"{_model.Notes}\""]
                """,
                Notes:
                "Textareas always stream — Textarea.Bound wires OnInputAsync for every keystroke so the echo updates without blur or submit, no matter how long the text is.",
                Result: BindingTextareaDemo()),
            H2(Class: "h4 mt-5 mb-3")["Typed — AfterBind (sync)"],
            CodeSample(
                """
                Select(Bind: () => _model.Country,
                       AfterBind: c => {
                           _cities = Cities[c];
                           _model.City = _cities[0];
                       })[ ... ]
                Select(Bind: () => _model.City)[
                    _cities.Select(c => Option(Value: c)[c])
                ]
                """,
                Notes:
                "AfterBind fires after TrySetTyped has written the new value to the model and before validators run. The dependent dropdown rebinds in the same render — no extra round-trip.",
                Result: BindingAfterBindDemo()),
            H2(Class: "h4 mt-5 mb-3")["Typed — AfterBindAsync (async)"],
            CodeSample(
                """
                Select(Bind: () => _model.Track,
                       AfterBindAsync: async track => {
                           _loading = true;
                           StateHasChanged();          // surface "loading…" mid-await
                           await Task.Delay(300);      // simulated fetch
                           _languages = Catalog[track];
                           _model.Language = _languages[0];
                           _loading = false;
                       })[ ... ]
                """,
                Notes:
                "AfterBindAsync is awaited before the post-handler render, so validators see the new dependent state. A mid-await StateHasChanged() pushes the \"loading…\" UI before the simulated fetch completes; the dispatcher's per-await rendering picks it up.",
                Result: BindingAfterBindAsyncDemo())
        ];
}
